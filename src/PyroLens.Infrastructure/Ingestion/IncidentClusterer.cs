using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Ingestion;

public sealed record ClusteringResult(int Candidates, int AttachedToExisting, int NewIncidents);

/// <summary>
/// Turns unassigned fire detections into incidents, in one transaction:
/// 1. pick recent detections that pass the confidence filter and are not likely industrial;
/// 2. attach each one to the nearest open incident within AttachDistanceMeters;
/// 3. group the rest with ST_ClusterDBSCAN (in EPSG:2100, Greek Grid, so distances are metres)
///    and open one incident per cluster;
/// 4. recompute centroid, footprint and counters of every incident that changed.
/// </summary>
public sealed class IncidentClusterer(
    PyroLensDbContext db,
    IOptions<ClusteringOptions> clustering,
    IOptions<DetectionFilterOptions> filter,
    TimeProvider clock)
{
    private const long ClusteringLockKey = 7_212_002;

    private const string AttachSql = """
        UPDATE fire_detections f
        SET incident_id = nearest.incident_id
        FROM unnest(@ids) AS c(id)
        JOIN fire_detections cd ON cd.id = c.id
        CROSS JOIN LATERAL (
            SELECT d.incident_id
            FROM fire_detections d
            JOIN incidents i ON i.id = d.incident_id
            WHERE i.status <> 'closed'
              AND i.last_detected_at >= @active_since
              AND ST_DWithin(d.location::geography, cd.location::geography, @attach_m)
            ORDER BY d.location::geography <-> cd.location::geography
            LIMIT 1
        ) AS nearest
        WHERE f.id = c.id
        RETURNING f.incident_id
        """;

    private const string ClusterSql = """
        -- CREATE TABLE AS cannot take parameters, so create first and fill with INSERT.
        CREATE TEMP TABLE _clusters (id bigint PRIMARY KEY, cluster_id integer NOT NULL) ON COMMIT DROP;

        INSERT INTO _clusters (id, cluster_id)
        SELECT id,
               ST_ClusterDBSCAN(ST_Transform(location, 2100), eps := @cluster_m, minpoints := 1) OVER ()
        FROM fire_detections
        WHERE id = ANY(@ids) AND incident_id IS NULL;

        CREATE TEMP TABLE _new_incidents ON COMMIT DROP AS
        SELECT cluster_id, nextval(pg_get_serial_sequence('incidents', 'id')) AS incident_id
        FROM (SELECT DISTINCT cluster_id FROM _clusters) AS s;

        INSERT INTO incidents (id, status, centroid, footprint, first_detected_at, last_detected_at, detection_count, max_frp_mw)
        SELECT n.incident_id,
               'detected',
               ST_Centroid(ST_Collect(f.location)),
               ST_ConvexHull(ST_Collect(f.location)),
               min(f.acquired_at),
               max(f.acquired_at),
               count(*),
               max(f.frp_mw)
        FROM _clusters c
        JOIN _new_incidents n USING (cluster_id)
        JOIN fire_detections f ON f.id = c.id
        GROUP BY n.incident_id;

        UPDATE fire_detections f
        SET incident_id = n.incident_id
        FROM _clusters c
        JOIN _new_incidents n USING (cluster_id)
        WHERE f.id = c.id;

        SELECT count(*)::int FROM _new_incidents;
        """;

    private const string RefreshSql = """
        UPDATE incidents i
        SET centroid          = s.centroid,
            footprint         = s.footprint,
            first_detected_at = s.first_detected_at,
            last_detected_at  = s.last_detected_at,
            detection_count   = s.detection_count,
            max_frp_mw        = s.max_frp_mw,
            updated_at        = now()
        FROM (
            SELECT incident_id,
                   ST_Centroid(ST_Collect(location))   AS centroid,
                   ST_ConvexHull(ST_Collect(location)) AS footprint,
                   min(acquired_at)                    AS first_detected_at,
                   max(acquired_at)                    AS last_detected_at,
                   count(*)                            AS detection_count,
                   max(frp_mw)                         AS max_frp_mw
            FROM fire_detections
            WHERE incident_id = ANY(@incident_ids)
            GROUP BY incident_id
        ) AS s
        WHERE i.id = s.incident_id
        """;

    public async Task<ClusteringResult> RunAsync(CancellationToken ct)
    {
        var c = clustering.Value;
        var f = filter.Value;
        var now = clock.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var npgsqlTx = (NpgsqlTransaction)tx.GetDbTransaction();

        // One clusterer at a time, even with several worker instances.
        await using (var lockCmd = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({ClusteringLockKey})", connection, npgsqlTx))
        {
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        var since = now.AddHours(-c.WindowHours);
        var candidateIds = await db.Detections
            .Where(d => d.IncidentId == null && !d.LikelyIndustrial && d.AcquiredAt >= since)
            .WherePassesConfidence(f.ModisConfidenceAbove, DetectionQueries.ParseViirsClasses(f.ViirsConfidence))
            .Select(d => d.Id)
            .ToArrayAsync(ct);

        if (candidateIds.Length == 0)
        {
            await tx.CommitAsync(ct);
            return new ClusteringResult(0, 0, 0);
        }

        var touchedIncidents = new HashSet<long>();
        var attached = 0;
        await using (var attach = new NpgsqlCommand(AttachSql, connection, npgsqlTx))
        {
            attach.Parameters.AddWithValue("ids", candidateIds);
            attach.Parameters.AddWithValue("active_since", now.AddHours(-c.IncidentActiveHours).UtcDateTime);
            attach.Parameters.AddWithValue("attach_m", c.AttachDistanceMeters);

            await using var reader = await attach.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                touchedIncidents.Add(reader.GetInt64(0));
                attached++;
            }
        }

        int newIncidents;
        await using (var cluster = new NpgsqlCommand(ClusterSql, connection, npgsqlTx))
        {
            cluster.Parameters.AddWithValue("ids", candidateIds);
            cluster.Parameters.AddWithValue("cluster_m", c.ClusterDistanceMeters);
            newIncidents = (int)(await cluster.ExecuteScalarAsync(ct))!;
        }

        if (touchedIncidents.Count > 0)
        {
            await using var refresh = new NpgsqlCommand(RefreshSql, connection, npgsqlTx);
            refresh.Parameters.AddWithValue("incident_ids", touchedIncidents.ToArray());
            await refresh.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new ClusteringResult(candidateIds.Length, attached, newIncidents);
    }
}
