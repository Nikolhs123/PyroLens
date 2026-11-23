using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using PyroLens.Core.Detections;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Ingestion;

public sealed record WriteResult(int Received, int Inserted, int FlaggedIndustrial);

/// <summary>
/// Stores a batch of detections with one INSERT … SELECT FROM unnest(arrays) statement.
/// Rows already stored are skipped through the natural-key unique constraint, so re-fetching is safe.
/// </summary>
public sealed class DetectionWriter(PyroLensDbContext db, IOptions<DetectionFilterOptions> filter)
{
    private const string InsertSql = """
        INSERT INTO fire_detections (
            source, instrument, satellite, latitude, longitude, acquired_at,
            confidence_raw, confidence_class, confidence_pct, brightness_k, frp_mw, is_daytime)
        SELECT @source, u.*
        FROM unnest(
            @instrument, @satellite, @latitude, @longitude, @acquired_at,
            @confidence_raw, @confidence_class, @confidence_pct, @brightness_k, @frp_mw, @is_daytime) AS u
        ON CONFLICT ON CONSTRAINT uq_fire_detections_natural_key DO NOTHING
        RETURNING id
        """;

    // A detection is a likely industrial source when fire was seen within the radius
    // on every one of the N calendar days (UTC) before it.
    private const string FlagIndustrialSql = """
        UPDATE fire_detections d
        SET likely_industrial = true
        WHERE d.id = ANY(@ids)
          AND (
              SELECT count(DISTINCT (p.acquired_at AT TIME ZONE 'UTC')::date)
              FROM fire_detections p
              WHERE ST_DWithin(p.location::geography, d.location::geography, @radius_m)
                AND (p.acquired_at AT TIME ZONE 'UTC')::date
                    BETWEEN (d.acquired_at AT TIME ZONE 'UTC')::date - @days
                        AND (d.acquired_at AT TIME ZONE 'UTC')::date - 1
          ) >= @days
        """;

    public async Task<WriteResult> WriteAsync(string source, IReadOnlyList<RawDetection> detections, CancellationToken ct)
    {
        if (detections.Count == 0)
        {
            return new WriteResult(0, 0, 0);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var npgsqlTx = (NpgsqlTransaction)tx.GetDbTransaction();

        var insertedIds = new List<long>(detections.Count);
        await using (var insert = new NpgsqlCommand(InsertSql, connection, npgsqlTx))
        {
            insert.Parameters.AddWithValue("source", source);
            Add(insert, "instrument", NpgsqlDbType.Text, detections.Select(d => d.Instrument.ToString().ToUpperInvariant()));
            Add(insert, "satellite", NpgsqlDbType.Text, detections.Select(d => d.Satellite));
            Add(insert, "latitude", NpgsqlDbType.Double, detections.Select(d => d.Latitude));
            Add(insert, "longitude", NpgsqlDbType.Double, detections.Select(d => d.Longitude));
            Add(insert, "acquired_at", NpgsqlDbType.TimestampTz, detections.Select(d => d.AcquiredAt.UtcDateTime));
            Add(insert, "confidence_raw", NpgsqlDbType.Text, detections.Select(d => d.ConfidenceRaw));
            Add(insert, "confidence_class", NpgsqlDbType.Text, detections.Select(d => d.ConfidenceClass?.ToString().ToLowerInvariant()));
            Add(insert, "confidence_pct", NpgsqlDbType.Smallint, detections.Select(d => d.ConfidencePercent));
            Add(insert, "brightness_k", NpgsqlDbType.Double, detections.Select(d => d.BrightnessKelvin));
            Add(insert, "frp_mw", NpgsqlDbType.Double, detections.Select(d => d.FrpMegawatts));
            Add(insert, "is_daytime", NpgsqlDbType.Boolean, detections.Select(d => d.IsDaytime));

            await using var reader = await insert.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                insertedIds.Add(reader.GetInt64(0));
            }
        }

        var flagged = 0;
        if (insertedIds.Count > 0)
        {
            await using var flag = new NpgsqlCommand(FlagIndustrialSql, connection, npgsqlTx);
            flag.Parameters.AddWithValue("ids", insertedIds.ToArray());
            flag.Parameters.AddWithValue("radius_m", filter.Value.IndustrialRadiusMeters);
            flag.Parameters.AddWithValue("days", filter.Value.IndustrialConsecutiveDays);
            flagged = await flag.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new WriteResult(detections.Count, insertedIds.Count, flagged);
    }

    private static void Add<T>(NpgsqlCommand cmd, string name, NpgsqlDbType elementType, IEnumerable<T> values) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | elementType) { Value = values.ToArray() });
}
