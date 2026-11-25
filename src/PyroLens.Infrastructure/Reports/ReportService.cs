using Microsoft.EntityFrameworkCore;
using PyroLens.Core.Reports;
using PyroLens.Infrastructure.Operations;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Reports;

/// <summary>A report with the open fire nearest to it, if one is within <see cref="ReportRules.NearbyMeters"/>.</summary>
public sealed record ReportView(
    long Id,
    double Latitude,
    double Longitude,
    ReportKind Kind,
    string? Note,
    DateTimeOffset SubmittedAt,
    ReportStatus Status,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    long? NearestIncidentId,
    double? NearestIncidentMeters);

/// <summary>Reports from the public: sending one, listing them for dispatchers, and marking them seen or dismissed.</summary>
public sealed class ReportService(PyroLensDbContext db, TimeProvider clock)
{
    public async Task<OperationResult<ReportView>> SubmitAsync(double latitude, double longitude, ReportKind kind, string? note, CancellationToken ct)
    {
        if (!ReportRules.IsValidCoordinate(latitude, longitude))
        {
            return OperationResult<ReportView>.Rejected("That is not a valid location.");
        }

        var cleaned = ReportRules.CleanNote(note);
        if (cleaned is { Length: > ReportRules.MaxNoteLength })
        {
            return OperationResult<ReportView>.Rejected($"Keep the note under {ReportRules.MaxNoteLength} characters.");
        }

        var now = clock.GetUtcNow();
        var kindText = PyroLensDbContext.ToSnakeCase(kind);

        // Inserted only when the point is in Greece (plus 2 km of sea): the same boundary the satellite filter uses,
        // checked by PostGIS in the same statement.
        var inserted = await db.Database.SqlQuery<long>($"""
            INSERT INTO citizen_reports (location, kind, note, submitted_at)
            SELECT p.point, {kindText}, {cleaned}, {now}
            FROM (SELECT ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326) AS point) AS p
            WHERE EXISTS (SELECT 1 FROM area_of_interest_parts a WHERE ST_Intersects(a.geom, p.point))
            RETURNING id AS "Value"
            """).ToListAsync(ct);

        if (inserted.Count == 0)
        {
            return OperationResult<ReportView>.Rejected(
                "That spot is outside Greece or out at sea. PyroLens covers Greece and the first 2 km off its coasts.");
        }

        var view = (await QueryAsync(inserted[0], since: null, includeDismissed: true, ct)).Single();
        return OperationResult<ReportView>.Success(view);
    }

    /// <summary>Reports sent since <paramref name="since"/>, newest first, each with the nearest open fire.</summary>
    public Task<List<ReportView>> ListAsync(DateTimeOffset since, bool includeDismissed, CancellationToken ct) =>
        QueryAsync(id: null, since, includeDismissed, ct);

    /// <summary>Marks a report seen or dismissed. Dismissed is final; doing the same thing twice is a conflict.</summary>
    public async Task<OperationResult<ReportView>> ReviewAsync(long id, ReportStatus to, string actor, CancellationToken ct)
    {
        if (to == ReportStatus.New)
        {
            return OperationResult<ReportView>.Rejected("A report can be marked seen or dismissed, not new again.");
        }

        var now = clock.GetUtcNow();
        var updated = await db.Reports
            .Where(r => r.Id == id && r.Status != to && r.Status != ReportStatus.Dismissed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, to)
                .SetProperty(r => r.ReviewedBy, actor)
                .SetProperty(r => r.ReviewedAt, now), ct);

        if (updated == 0)
        {
            var current = await db.Reports.Where(r => r.Id == id).Select(r => (ReportStatus?)r.Status).FirstOrDefaultAsync(ct);
            return current is null
                ? OperationResult<ReportView>.Missing($"Report {id} does not exist.")
                : OperationResult<ReportView>.Conflicted($"Report {id} is already {current.Value.ToString().ToLowerInvariant()}.");
        }

        return OperationResult<ReportView>.Success((await QueryAsync(id, since: null, includeDismissed: true, ct)).Single());
    }

    private async Task<List<ReportView>> QueryAsync(long? id, DateTimeOffset? since, bool includeDismissed, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ReportRow>($"""
            SELECT r.id AS "Id", ST_Y(r.location) AS "Latitude", ST_X(r.location) AS "Longitude",
                   r.kind AS "Kind", r.note AS "Note", r.submitted_at AS "SubmittedAt", r.status AS "Status",
                   r.reviewed_by AS "ReviewedBy", r.reviewed_at AS "ReviewedAt",
                   n.id AS "NearestIncidentId", n.meters AS "NearestIncidentMeters"
            FROM citizen_reports r
            LEFT JOIN LATERAL (
                SELECT i.id, ST_Distance(i.centroid::geography, r.location::geography) AS meters
                FROM incidents i
                WHERE i.status <> 'closed'
                  AND ST_DWithin(i.centroid::geography, r.location::geography, {ReportRules.NearbyMeters})
                ORDER BY i.centroid::geography <-> r.location::geography
                LIMIT 1
            ) AS n ON true
            WHERE ({id}::bigint IS NULL OR r.id = {id})
              AND ({since}::timestamptz IS NULL OR r.submitted_at >= {since})
              AND ({includeDismissed} OR r.status <> 'dismissed')
            ORDER BY r.submitted_at DESC
            LIMIT 200
            """).ToListAsync(ct);

        return rows.Select(r => r.ToView()).ToList();
    }

    internal sealed class ReportRow
    {
        public long Id { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Kind { get; set; } = "";
        public string? Note { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string Status { get; set; } = "";
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public long? NearestIncidentId { get; set; }
        public double? NearestIncidentMeters { get; set; }

        public ReportView ToView() => new(
            Id, Latitude, Longitude,
            PyroLensDbContext.FromSnakeCase<ReportKind>(Kind), Note, Utc(SubmittedAt),
            PyroLensDbContext.FromSnakeCase<ReportStatus>(Status), ReviewedBy, ReviewedAt is { } at ? Utc(at) : null,
            NearestIncidentId, NearestIncidentMeters is { } m ? Math.Round(m) : null);

        private static DateTimeOffset Utc(DateTime t) => new(DateTime.SpecifyKind(t, DateTimeKind.Utc));
    }
}
