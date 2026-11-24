using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Npgsql;
using PyroLens.Core.Forecasting;
using PyroLens.Core.Incidents;
using PyroLens.Infrastructure.Persistence;
using PyroLens.Infrastructure.Weather;

namespace PyroLens.Infrastructure.Forecasting;

public sealed record ForecastCycleResult(int ActiveIncidents, int ForecastsWritten, int Skipped, int Deleted = 0);

/// <summary>
/// One forecast cycle: weather for every active incident's centre, then +1 h and +3 h spread areas.
/// Rows are upserted per (incident, forecast hour, horizon), so repeating a cycle within the hour is harmless.
/// </summary>
public sealed class ForecastService(
    PyroLensDbContext db,
    IWeatherProvider weather,
    IOptions<ForecastOptions> options,
    TimeProvider clock,
    ILogger<ForecastService> logger)
{
    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    private const string UpsertSql = """
        INSERT INTO incident_forecasts (
            incident_id, issued_at, weather_from, horizon_hours,
            mean_temperature_c, mean_relative_humidity_pct, mean_wind_speed_kmh, mean_wind_from_deg, max_wind_gust_kmh,
            drought_factor, fuel_load_t_ha, peak_ffdi, danger_rating, peak_rate_of_spread_m_h,
            head_distance_m, length_to_breadth, spread_bearing_deg, spread_area, model)
        SELECT @incident_id, @issued_at, @weather_from, @horizon_hours,
               @temperature, @humidity, @wind_speed, @wind_from, @gust,
               @drought_factor, @fuel_load, @ffdi, @rating, @ros,
               @head, @lb, @bearing,
               -- The fire can be anywhere it already burns plus the ellipse.
               ST_ConvexHull(ST_Collect(@ellipse, coalesce(i.footprint, i.centroid))),
               @model
        FROM incidents i
        WHERE i.id = @incident_id
        ON CONFLICT ON CONSTRAINT uq_incident_forecasts_hour DO UPDATE SET
            issued_at = EXCLUDED.issued_at,
            mean_temperature_c = EXCLUDED.mean_temperature_c,
            mean_relative_humidity_pct = EXCLUDED.mean_relative_humidity_pct,
            mean_wind_speed_kmh = EXCLUDED.mean_wind_speed_kmh,
            mean_wind_from_deg = EXCLUDED.mean_wind_from_deg,
            max_wind_gust_kmh = EXCLUDED.max_wind_gust_kmh,
            drought_factor = EXCLUDED.drought_factor,
            fuel_load_t_ha = EXCLUDED.fuel_load_t_ha,
            peak_ffdi = EXCLUDED.peak_ffdi,
            danger_rating = EXCLUDED.danger_rating,
            peak_rate_of_spread_m_h = EXCLUDED.peak_rate_of_spread_m_h,
            head_distance_m = EXCLUDED.head_distance_m,
            length_to_breadth = EXCLUDED.length_to_breadth,
            spread_bearing_deg = EXCLUDED.spread_bearing_deg,
            spread_area = EXCLUDED.spread_area,
            model = EXCLUDED.model
        """;

    public async Task<ForecastCycleResult> RunOnceAsync(CancellationToken ct)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var since = now.AddHours(-o.ActiveIncidentHours);

        // Keep the database small (free tiers cap it at 500 MB); old forecasts have no use once the hour has passed.
        var cutoff = now.AddDays(-o.RetentionDays);
        var deleted = await db.Forecasts.Where(f => f.WeatherFrom < cutoff).ExecuteDeleteAsync(ct);

        var incidents = await db.Incidents.AsNoTracking()
            // Recently detected, or being worked on by a dispatcher even without new detections.
            .Where(i => i.Status != IncidentStatus.Closed &&
                        (i.LastDetectedAt >= since ||
                         i.Status == IncidentStatus.Confirmed || i.Status == IncidentStatus.Active || i.Status == IncidentStatus.Contained))
            .OrderBy(i => i.Id)
            .Select(i => new { i.Id, Latitude = i.Centroid.Y, Longitude = i.Centroid.X })
            .ToListAsync(ct);

        if (incidents.Count == 0)
        {
            return new ForecastCycleResult(0, 0, 0, deleted);
        }

        var maxHorizon = ForecastOptions.Horizons.Max();
        var origins = incidents.Select(i => new GeoPoint(i.Latitude, i.Longitude)).ToList();
        var hourly = await weather.GetHourlyAsync(origins, maxHorizon, ct);
        var settings = new SpreadSettings(o.DroughtFactor, o.FuelLoadTonnesPerHectare);

        await using var batch = new NpgsqlBatch();
        var skipped = 0;

        for (var i = 0; i < incidents.Count; i++)
        {
            if (hourly[i].Count < maxHorizon)
            {
                skipped++;
                logger.LogWarning("Incident {IncidentId}: only {Hours} hours of weather, skipped", incidents[i].Id, hourly[i].Count);
                continue;
            }

            foreach (var horizon in ForecastOptions.Horizons)
            {
                var f = SpreadForecaster.Forecast(origins[i], hourly[i], horizon, settings);
                batch.BatchCommands.Add(ToCommand(incidents[i].Id, now, f, settings));
            }
        }

        if (batch.BatchCommands.Count > 0)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            batch.Connection = (NpgsqlConnection)db.Database.GetDbConnection();
            batch.Transaction = (NpgsqlTransaction)tx.GetDbTransaction();
            await batch.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }

        return new ForecastCycleResult(incidents.Count, batch.BatchCommands.Count, skipped, deleted);
    }

    private static NpgsqlBatchCommand ToCommand(long incidentId, DateTimeOffset issuedAt, HorizonForecast f, SpreadSettings settings)
    {
        var ring = f.Ellipse.Select(p => new Coordinate(p.Longitude, p.Latitude)).ToArray();

        var cmd = new NpgsqlBatchCommand(UpsertSql);
        cmd.Parameters.AddWithValue("incident_id", incidentId);
        cmd.Parameters.AddWithValue("issued_at", issuedAt.UtcDateTime);
        cmd.Parameters.AddWithValue("weather_from", f.WeatherFrom.UtcDateTime);
        cmd.Parameters.AddWithValue("horizon_hours", (short)f.HorizonHours);
        cmd.Parameters.AddWithValue("temperature", f.MeanTemperatureC);
        cmd.Parameters.AddWithValue("humidity", Math.Clamp(f.MeanRelativeHumidityPercent, 0, 100));
        cmd.Parameters.AddWithValue("wind_speed", f.MeanWindSpeedKmh);
        cmd.Parameters.AddWithValue("wind_from", f.MeanWindFromDegrees);
        cmd.Parameters.AddWithValue("gust", (object?)f.MaxWindGustKmh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("drought_factor", settings.DroughtFactor);
        cmd.Parameters.AddWithValue("fuel_load", settings.FuelLoadTonnesPerHectare);
        cmd.Parameters.AddWithValue("ffdi", f.PeakFfdi);
        cmd.Parameters.AddWithValue("rating", PyroLensDbContext.ToDbValue(f.Rating));
        cmd.Parameters.AddWithValue("ros", f.PeakRateOfSpreadMetersPerHour);
        cmd.Parameters.AddWithValue("head", f.HeadDistanceMeters);
        cmd.Parameters.AddWithValue("lb", f.LengthToBreadth);
        cmd.Parameters.AddWithValue("bearing", f.SpreadBearingDegrees);
        cmd.Parameters.AddWithValue("ellipse", Geometry.CreatePolygon(ring));
        cmd.Parameters.AddWithValue("model", FireBehaviour.ModelName);
        return cmd;
    }
}
