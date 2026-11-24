using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using PyroLens.Core.Incidents;
using PyroLens.Core.Resources;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Operations;

/// <summary>
/// Airspace reservations around a fire. Overlaps are not checked here: the exclusion constraint
/// ex_airspace_no_overlap rejects them inside PostgreSQL, so two concurrent requests cannot both win.
/// </summary>
public sealed class AirspaceOperations(PyroLensDbContext db, TimeProvider clock, IOptions<OperationsOptions> options)
{
    private const string ExclusionViolation = "23P01";

    private const string InsertSql = """
        INSERT INTO airspace_reservations (incident_id, level, area, radius_m, during, reserved_by, created_at)
        SELECT i.id, @level, ST_Buffer(i.centroid::geography, @radius)::geometry, @radius,
               tstzrange(@starts, @ends, '[)'), @actor, @now
        FROM incidents i
        WHERE i.id = @incident
        RETURNING id
        """;

    public async Task<OperationResult<AirspaceReservation>> ReserveAsync(
        long incidentId, AirspaceLevel level, double radiusMeters, DateTimeOffset startsAt, DateTimeOffset endsAt,
        string actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        if (radiusMeters is < 200 or > 50_000)
        {
            return OperationResult<AirspaceReservation>.Rejected("The radius must be between 200 m and 50 km.");
        }

        if (endsAt <= startsAt)
        {
            return OperationResult<AirspaceReservation>.Rejected("The window must end after it starts.");
        }

        if (endsAt - startsAt > options.Value.MaxAirspaceWindow)
        {
            return OperationResult<AirspaceReservation>.Rejected($"A reservation may cover at most {options.Value.MaxAirspaceWindow.TotalHours:0} hours.");
        }

        if (endsAt <= now)
        {
            return OperationResult<AirspaceReservation>.Rejected("The window is already over.");
        }

        long id;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var status = (await db.Database
                    .SqlQuery<string>($"""SELECT status AS "Value" FROM incidents WHERE id = {incidentId} FOR SHARE""")
                    .ToListAsync(ct))
                .SingleOrDefault();

            if (status is null)
            {
                return OperationResult<AirspaceReservation>.Missing($"Incident {incidentId} does not exist.");
            }

            var parsed = Enum.Parse<IncidentStatus>(status, ignoreCase: true);
            if (!IncidentLifecycle.AcceptsResources(parsed))
            {
                return OperationResult<AirspaceReservation>.Rejected(
                    $"Incident {incidentId} is {parsed}. Airspace can be reserved for confirmed, active or contained incidents.");
            }

            try
            {
                await using var insert = new NpgsqlCommand(InsertSql, (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)tx.GetDbTransaction());
                insert.Parameters.AddWithValue("level", PyroLensDbContext.ToSnakeCase(level));
                insert.Parameters.AddWithValue("radius", radiusMeters);
                insert.Parameters.AddWithValue("starts", startsAt.UtcDateTime);
                insert.Parameters.AddWithValue("ends", endsAt.UtcDateTime);
                insert.Parameters.AddWithValue("actor", actor);
                insert.Parameters.AddWithValue("now", now.UtcDateTime);
                insert.Parameters.AddWithValue("incident", incidentId);
                id = (long)(await insert.ExecuteScalarAsync(ct))!;
            }
            catch (PostgresException ex) when (ex.SqlState == ExclusionViolation)
            {
                await tx.RollbackAsync(ct);
                return OperationResult<AirspaceReservation>.Conflicted(await DescribeClashAsync(incidentId, level, radiusMeters, startsAt, endsAt, ct));
            }

            db.Events.Add(IncidentOperations.Event(incidentId, now, IncidentEventType.AirspaceReserved, actor,
                details: new { reservationId = id, level = PyroLensDbContext.ToSnakeCase(level), radiusMeters, startsAt, endsAt }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return OperationResult<AirspaceReservation>.Success(await db.Airspace.AsNoTracking().SingleAsync(a => a.Id == id, ct));
    }

    public async Task<OperationResult<AirspaceReservation>> CancelAsync(long reservationId, string actor, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var updated = await db.Airspace
            .Where(a => a.Id == reservationId && a.CancelledAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CancelledAt, now), ct);

        var reservation = await db.Airspace.AsNoTracking().SingleOrDefaultAsync(a => a.Id == reservationId, ct);
        if (reservation is null)
        {
            return OperationResult<AirspaceReservation>.Missing($"Airspace reservation {reservationId} does not exist.");
        }

        if (updated == 0)
        {
            return OperationResult<AirspaceReservation>.Conflicted($"Airspace reservation {reservationId} was already cancelled.");
        }

        db.Events.Add(IncidentOperations.Event(reservation.IncidentId, now, IncidentEventType.AirspaceCancelled, actor,
            details: new { reservationId }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return OperationResult<AirspaceReservation>.Success(reservation);
    }

    /// <summary>Reservations not cancelled and not yet over, optionally for one incident.</summary>
    public async Task<List<AirspaceReservation>> ListActiveAsync(long? incidentId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.Airspace.AsNoTracking().Where(a => a.CancelledAt == null);
        if (incidentId is { } id)
        {
            query = query.Where(a => a.IncidentId == id);
        }

        // Few rows are live at any time; the end-of-window check is simplest after loading.
        var live = await query.ToListAsync(ct);
        return live.Where(a => a.During.UpperBound > now).OrderBy(a => a.During.LowerBound).ToList();
    }

    /// <summary>Names the reservation that blocked the request, for a useful 409 message.</summary>
    private async Task<string> DescribeClashAsync(long incidentId, AirspaceLevel level, double radius, DateTimeOffset starts, DateTimeOffset ends, CancellationToken ct)
    {
        var levelText = PyroLensDbContext.ToSnakeCase(level);
        var clash = (await db.Database.SqlQuery<Clash>($"""
                SELECT a.id AS "Id", a.incident_id AS "IncidentId", lower(a.during) AS "From", upper(a.during) AS "To"
                FROM airspace_reservations a CROSS JOIN incidents i
                WHERE i.id = {incidentId}
                  AND a.cancelled_at IS NULL
                  AND a.level = {levelText}
                  AND a.area && ST_Buffer(i.centroid::geography, {radius})::geometry
                  AND a.during && tstzrange({starts.UtcDateTime}, {ends.UtcDateTime}, '[)')
                ORDER BY lower(a.during)
                LIMIT 1
                """).ToListAsync(ct)).FirstOrDefault();

        var band = level == AirspaceLevel.Low ? "Low (drone)" : "High (aircraft)";
        return clash is null
            ? $"{band} airspace overlaps another reservation there."
            : $"{band} airspace there is already reserved {clash.From:HH:mm}–{clash.To:HH:mm} UTC by reservation {clash.Id} (incident {clash.IncidentId}).";
    }

}

internal sealed class Clash
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
