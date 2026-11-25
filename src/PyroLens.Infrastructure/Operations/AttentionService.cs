using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PyroLens.Core.Incidents;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Operations;

public sealed class AttentionOptions
{
    public const string Section = "Attention";

    /// <summary>A confirmed or active fire with no unit on it for this long is urgent.</summary>
    public TimeSpan NoUnitsAfter { get; set; } = AttentionSettings.Default.NoUnitsAfter;

    /// <summary>This many new satellite pixels within <see cref="SpreadingWindow"/> means the fire is spreading.</summary>
    [Range(1, 100)]
    public int SpreadingPixels { get; set; } = AttentionSettings.Default.SpreadingPixels;

    public TimeSpan SpreadingWindow { get; set; } = AttentionSettings.Default.SpreadingWindow;

    public AttentionSettings ToSettings() => new(NoUnitsAfter, SpreadingPixels, SpreadingWindow);
}

/// <summary>
/// Gathers the facts the escalation rules need for a set of incidents in one query, then applies
/// <see cref="AttentionRules"/>. Runs on every read, so a time-based flag ("no units for 20 minutes") is always exact.
/// </summary>
public sealed class AttentionService(PyroLensDbContext db, TimeProvider clock, IOptions<AttentionOptions> options)
{
    public async Task<Dictionary<long, IReadOnlyList<Attention>>> EvaluateAsync(IReadOnlyCollection<long> incidentIds, CancellationToken ct)
    {
        if (incidentIds.Count == 0)
        {
            return [];
        }

        var now = clock.GetUtcNow();
        var settings = options.Value.ToSettings();
        var recentSince = now - settings.SpreadingWindow;
        var horizon = ForecastOptions.Horizons.Max();
        var ids = incidentIds.ToArray();

        // One row per incident. The events subquery reads the append-only timeline for when a dispatcher took the
        // fire on, when it was contained, and when its last unit was released.
        var rows = await db.Database.SqlQuery<FactsRow>($"""
            SELECT i.id AS "IncidentId",
                   i.status AS "Status",
                   (SELECT count(*)::int FROM fire_detections d
                     WHERE d.incident_id = i.id AND d.acquired_at >= {recentSince}) AS "RecentPixels",
                   (SELECT count(*)::int FROM fire_detections d
                     WHERE d.incident_id = i.id AND d.acquired_at > ev.contained_at) AS "PixelsSinceContained",
                   (SELECT f.danger_rating FROM incident_forecasts f
                     WHERE f.incident_id = i.id AND f.horizon_hours = {horizon}
                     ORDER BY f.weather_from DESC LIMIT 1) AS "LatestRating",
                   (SELECT f.danger_rating FROM incident_forecasts f
                     WHERE f.incident_id = i.id AND f.horizon_hours = {horizon}
                     ORDER BY f.weather_from DESC OFFSET 1 LIMIT 1) AS "PreviousRating",
                   (SELECT count(*)::int FROM resources r
                     WHERE r.incident_id = i.id AND r.status = 'assigned') AS "AssignedUnits",
                   ev.managed_since AS "ManagedSince",
                   ev.contained_at AS "ContainedAt",
                   ev.last_released_at AS "LastReleasedAt"
            FROM incidents i
            LEFT JOIN LATERAL (
                SELECT max(e.occurred_at) FILTER (
                           WHERE e.type = 'status_changed'
                             AND (e.to_status = 'confirmed' OR (e.to_status = 'active' AND e.from_status = 'contained'))
                       ) AS managed_since,
                       max(e.occurred_at) FILTER (WHERE e.type = 'status_changed' AND e.to_status = 'contained') AS contained_at,
                       max(e.occurred_at) FILTER (WHERE e.type = 'resource_released') AS last_released_at
                FROM incident_events e
                WHERE e.incident_id = i.id
            ) ev ON true
            WHERE i.id = ANY({ids})
            """).ToListAsync(ct);

        return rows.ToDictionary(r => r.IncidentId, r => AttentionRules.Evaluate(r.ToFacts(), now, settings));
    }

    internal sealed class FactsRow
    {
        public long IncidentId { get; set; }
        public string Status { get; set; } = "";
        public int RecentPixels { get; set; }
        public int PixelsSinceContained { get; set; }
        public string? LatestRating { get; set; }
        public string? PreviousRating { get; set; }
        public int AssignedUnits { get; set; }
        public DateTime? ManagedSince { get; set; }
        public DateTime? ContainedAt { get; set; }
        public DateTime? LastReleasedAt { get; set; }

        public AttentionFacts ToFacts() => new(
            Enum.Parse<IncidentStatus>(Status, ignoreCase: true),
            RecentPixels,
            PixelsSinceContained,
            LatestRating is null ? null : PyroLensDbContext.FromDbValue(LatestRating),
            PreviousRating is null ? null : PyroLensDbContext.FromDbValue(PreviousRating),
            AssignedUnits,
            Utc(ManagedSince),
            Utc(ContainedAt),
            Utc(LastReleasedAt));

        private static DateTimeOffset? Utc(DateTime? t) =>
            t is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;
    }
}
