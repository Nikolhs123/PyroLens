using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PyroLens.Core.Incidents;
using PyroLens.Core.Resources;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Operations;

public sealed record ResourceSuggestion(Resource Resource, double DistanceMeters, int EtaMinutes);

/// <summary>
/// Reserve (a short hold) → assign → release. Each step is one UPDATE whose WHERE clause states what must still
/// be true, so concurrent dispatchers cannot both get the same unit: the loser updates 0 rows and gets a conflict.
/// </summary>
public sealed class ResourceOperations(PyroLensDbContext db, TimeProvider clock, IOptions<OperationsOptions> options)
{
    public async Task<OperationResult<Resource>> ReserveAsync(long resourceId, long incidentId, string actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var until = now + options.Value.ReservationHold;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await CheckIncidentAsync(incidentId, ct) is { } problem)
        {
            return problem;
        }

        // Free, or held by a reservation that has run out.
        var updated = await db.Resources
            .Where(r => r.Id == resourceId &&
                        (r.Status == ResourceStatus.Available || (r.Status == ResourceStatus.Reserved && r.ReservedUntil < now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ResourceStatus.Reserved)
                .SetProperty(r => r.IncidentId, incidentId)
                .SetProperty(r => r.ReservedUntil, until)
                .SetProperty(r => r.UpdatedAt, now), ct);

        if (updated == 0)
        {
            return await ExplainAsync(resourceId, now, ct);
        }

        var resource = await db.Resources.AsNoTracking().SingleAsync(r => r.Id == resourceId, ct);
        db.Events.Add(IncidentOperations.Event(incidentId, now, IncidentEventType.ResourceReserved, actor,
            details: new { callsign = resource.Callsign, until }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return OperationResult<Resource>.Success(resource);
    }

    /// <summary>Turns this incident's live reservation into an assignment.</summary>
    public async Task<OperationResult<Resource>> AssignAsync(long resourceId, long incidentId, string actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await CheckIncidentAsync(incidentId, ct) is { } problem)
        {
            return problem;
        }

        var updated = await db.Resources
            .Where(r => r.Id == resourceId && r.Status == ResourceStatus.Reserved && r.IncidentId == incidentId && r.ReservedUntil >= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ResourceStatus.Assigned)
                .SetProperty(r => r.ReservedUntil, (DateTimeOffset?)null)
                .SetProperty(r => r.UpdatedAt, now), ct);

        if (updated == 0)
        {
            var current = await db.Resources.AsNoTracking().SingleOrDefaultAsync(r => r.Id == resourceId, ct);
            if (current is null)
            {
                return OperationResult<Resource>.Missing($"Resource {resourceId} does not exist.");
            }

            return current.Status == ResourceStatus.Assigned && current.IncidentId == incidentId
                ? OperationResult<Resource>.Conflicted($"{current.Callsign} is already assigned to incident {incidentId}.")
                : OperationResult<Resource>.Conflicted(
                    $"{current.Callsign} has no live reservation for incident {incidentId} ({Describe(current, now)}). Reserve it first.");
        }

        var resource = await db.Resources.AsNoTracking().SingleAsync(r => r.Id == resourceId, ct);
        db.Events.Add(IncidentOperations.Event(incidentId, now, IncidentEventType.ResourceAssigned, actor,
            details: new { callsign = resource.Callsign }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return OperationResult<Resource>.Success(resource);
    }

    /// <summary>Frees a unit reserved for or assigned to this incident.</summary>
    public async Task<OperationResult<Resource>> ReleaseAsync(long resourceId, long incidentId, string actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var updated = await db.Resources
            .Where(r => r.Id == resourceId && r.IncidentId == incidentId &&
                        (r.Status == ResourceStatus.Reserved || r.Status == ResourceStatus.Assigned))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ResourceStatus.Available)
                .SetProperty(r => r.IncidentId, (long?)null)
                .SetProperty(r => r.ReservedUntil, (DateTimeOffset?)null)
                .SetProperty(r => r.UpdatedAt, now), ct);

        var resource = await db.Resources.AsNoTracking().SingleOrDefaultAsync(r => r.Id == resourceId, ct);
        if (resource is null)
        {
            return OperationResult<Resource>.Missing($"Resource {resourceId} does not exist.");
        }

        if (updated == 0)
        {
            return OperationResult<Resource>.Conflicted($"{resource.Callsign} is not committed to incident {incidentId} ({Describe(resource, now)}).");
        }

        db.Events.Add(IncidentOperations.Event(incidentId, now, IncidentEventType.ResourceReleased, actor,
            details: new { callsign = resource.Callsign }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return OperationResult<Resource>.Success(resource);
    }

    /// <summary>All resources, with expired reservations shown as available.</summary>
    public async Task<List<Resource>> ListAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var resources = await db.Resources.AsNoTracking().OrderBy(r => r.Callsign).ToListAsync(ct);
        foreach (var r in resources.Where(r => r.Status == ResourceStatus.Reserved && r.ReservedUntil < now))
        {
            r.Status = ResourceStatus.Available;
            r.IncidentId = null;
            r.ReservedUntil = null;
        }

        return resources;
    }

    /// <summary>Available units ordered by estimated time of arrival at the incident's centre.</summary>
    public async Task<OperationResult<List<ResourceSuggestion>>> SuggestAsync(long incidentId, int limit, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!await db.Incidents.AnyAsync(i => i.Id == incidentId, ct))
        {
            return OperationResult<List<ResourceSuggestion>>.Missing($"Incident {incidentId} does not exist.");
        }

        // Distances on the spheroid, in metres.
        var distances = await db.Database
            .SqlQuery<ResourceDistance>($"""
                SELECT r.id AS "Id", ST_Distance(r.base_location::geography, i.centroid::geography) AS "Meters"
                FROM resources r CROSS JOIN incidents i
                WHERE i.id = {incidentId}
                """)
            .ToDictionaryAsync(d => d.Id, d => d.Meters, ct);

        var free = await db.Resources.AsNoTracking()
            .Where(r => r.Status == ResourceStatus.Available || (r.Status == ResourceStatus.Reserved && r.ReservedUntil < now))
            .ToListAsync(ct);

        var suggestions = free
            .Select(r => new ResourceSuggestion(r, distances[r.Id], ResourceKinds.EtaMinutes(r.Kind, distances[r.Id])))
            .OrderBy(s => s.EtaMinutes)
            .ThenBy(s => s.Resource.Callsign)
            .Take(limit)
            .ToList();

        return OperationResult<List<ResourceSuggestion>>.Success(suggestions);
    }

    /// <summary>
    /// Locks the incident row (FOR SHARE) for the rest of the transaction and checks it accepts resources.
    /// The lock makes a concurrent close wait for us, or us wait for it, so no unit is committed to a closed incident.
    /// </summary>
    private async Task<OperationResult<Resource>?> CheckIncidentAsync(long incidentId, CancellationToken ct)
    {
        var status = (await db.Database
                .SqlQuery<string>($"""SELECT status AS "Value" FROM incidents WHERE id = {incidentId} FOR SHARE""")
                .ToListAsync(ct))
            .SingleOrDefault();

        if (status is null)
        {
            return OperationResult<Resource>.Missing($"Incident {incidentId} does not exist.");
        }

        var parsed = Enum.Parse<IncidentStatus>(status, ignoreCase: true);
        return IncidentLifecycle.AcceptsResources(parsed)
            ? null
            : OperationResult<Resource>.Rejected(
                $"Incident {incidentId} is {parsed}. Resources can be committed to confirmed, active or contained incidents.");
    }

    private async Task<OperationResult<Resource>> ExplainAsync(long resourceId, DateTimeOffset now, CancellationToken ct)
    {
        var current = await db.Resources.AsNoTracking().SingleOrDefaultAsync(r => r.Id == resourceId, ct);
        return current is null
            ? OperationResult<Resource>.Missing($"Resource {resourceId} does not exist.")
            : OperationResult<Resource>.Conflicted($"{current.Callsign} is not available: {Describe(current, now)}.");
    }

    private static string Describe(Resource r, DateTimeOffset now) => r.Status switch
    {
        ResourceStatus.Reserved when r.ReservedUntil < now => "its reservation has expired",
        ResourceStatus.Reserved => $"reserved for incident {r.IncidentId} until {r.ReservedUntil:HH:mm:ss} UTC",
        ResourceStatus.Assigned => $"assigned to incident {r.IncidentId}",
        ResourceStatus.OutOfService => "out of service",
        _ => "available",
    };
}

internal sealed class ResourceDistance
{
    public long Id { get; set; }
    public double Meters { get; set; }
}
