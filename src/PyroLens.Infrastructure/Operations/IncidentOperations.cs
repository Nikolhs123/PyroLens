using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PyroLens.Core.Incidents;
using PyroLens.Core.Resources;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Operations;

public sealed class IncidentOperations(PyroLensDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Moves an incident from <paramref name="expected"/> to <paramref name="to"/>. The UPDATE only matches while the
    /// incident is still in the status the dispatcher saw, so two dispatchers cannot both move it: the second gets a
    /// conflict. Closing releases every resource and cancels every airspace reservation of the incident.
    /// </summary>
    public async Task<OperationResult<Incident>> ChangeStatusAsync(
        long id, IncidentStatus expected, IncidentStatus to, string actor, string? note, CancellationToken ct)
    {
        if (!IncidentLifecycle.CanTransition(expected, to))
        {
            return OperationResult<Incident>.Rejected($"An incident cannot go from {expected} to {to}.");
        }

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var updated = await db.Incidents
            .Where(i => i.Id == id && i.Status == expected)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, to).SetProperty(i => i.UpdatedAt, now), ct);

        if (updated == 0)
        {
            var current = await db.Incidents.Where(i => i.Id == id).Select(i => (IncidentStatus?)i.Status).SingleOrDefaultAsync(ct);
            return current is null
                ? OperationResult<Incident>.Missing($"Incident {id} does not exist.")
                : OperationResult<Incident>.Conflicted($"Incident {id} is already {current}, not {expected}. Reload and try again.");
        }

        db.Events.Add(Event(id, now, IncidentEventType.StatusChanged, actor, note, from: expected, to: to));

        if (to == IncidentStatus.Closed)
        {
            await ReleaseEverythingAsync(id, now, actor, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return OperationResult<Incident>.Success(await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == id, ct));
    }

    public Task<List<IncidentEvent>> TimelineAsync(long id, CancellationToken ct) =>
        db.Events.AsNoTracking().Where(e => e.IncidentId == id).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(ct);

    private async Task ReleaseEverythingAsync(long incidentId, DateTimeOffset now, string actor, CancellationToken ct)
    {
        var committed = await db.Resources.Where(r => r.IncidentId == incidentId).Select(r => r.Callsign).ToListAsync(ct);
        await db.Resources
            .Where(r => r.IncidentId == incidentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ResourceStatus.Available)
                .SetProperty(r => r.IncidentId, (long?)null)
                .SetProperty(r => r.ReservedUntil, (DateTimeOffset?)null)
                .SetProperty(r => r.UpdatedAt, now), ct);

        foreach (var callsign in committed)
        {
            db.Events.Add(Event(incidentId, now, IncidentEventType.ResourceReleased, actor, "Incident closed", details: new { callsign }));
        }

        var cancelled = await db.Airspace.Where(a => a.IncidentId == incidentId && a.CancelledAt == null).Select(a => a.Id).ToListAsync(ct);
        await db.Airspace
            .Where(a => a.IncidentId == incidentId && a.CancelledAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CancelledAt, now), ct);

        foreach (var reservationId in cancelled)
        {
            db.Events.Add(Event(incidentId, now, IncidentEventType.AirspaceCancelled, actor, "Incident closed", details: new { reservationId }));
        }
    }

    internal static IncidentEvent Event(
        long incidentId, DateTimeOffset at, IncidentEventType type, string actor, string? note = null,
        IncidentStatus? from = null, IncidentStatus? to = null, object? details = null) => new()
        {
            IncidentId = incidentId,
            OccurredAt = at,
            Type = type,
            Actor = actor,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            FromStatus = from,
            ToStatus = to,
            Details = details is null ? "{}" : JsonSerializer.Serialize(details, JsonOptions),
        };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
