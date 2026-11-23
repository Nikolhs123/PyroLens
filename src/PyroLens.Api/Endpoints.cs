using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PyroLens.Core.Detections;
using PyroLens.Core.Forecasting;
using PyroLens.Core.Incidents;
using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Api;

public sealed record IncidentDto(
    long Id,
    IncidentStatus Status,
    double Latitude,
    double Longitude,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastDetectedAt,
    int DetectionCount,
    double? MaxFrpMegawatts);

public sealed record DetectionDto(
    long Id,
    string Source,
    Instrument Instrument,
    double Latitude,
    double Longitude,
    DateTimeOffset AcquiredAt,
    string Confidence,
    double? FrpMegawatts,
    bool LikelyIndustrial,
    string? AreaCode,
    long? IncidentId);

public sealed record IncidentDetailDto(IncidentDto Incident, IReadOnlyList<DetectionDto> Detections);

public sealed record WeatherDto(
    double MeanTemperatureC,
    double MeanRelativeHumidityPercent,
    double MeanWindSpeedKmh,
    double MeanWindFromDegrees,
    double? MaxWindGustKmh);

/// <summary>GeoJSON polygon: coordinates are [longitude, latitude] rings.</summary>
public sealed record GeoJsonPolygon(string Type, double[][][] Coordinates);

public sealed record HorizonDto(
    int HorizonHours,
    WeatherDto Weather,
    double PeakFfdi,
    FireDangerRating DangerRating,
    double PeakRateOfSpreadMetersPerHour,
    double HeadDistanceMeters,
    double LengthToBreadth,
    double SpreadBearingDegrees,
    GeoJsonPolygon SpreadArea);

public sealed record ForecastDto(
    long IncidentId,
    DateTimeOffset IssuedAt,
    DateTimeOffset WeatherFrom,
    double DroughtFactor,
    double FuelLoadTonnesPerHectare,
    string Model,
    string Disclaimer,
    string Attribution,
    IReadOnlyList<HorizonDto> Horizons);

public static class Endpoints
{
    private const int MaxHours = 168;

    public static RouteGroupBuilder MapPyroLensEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/incidents", ListIncidents);
        api.MapGet("/incidents/{id:long}", GetIncident);
        api.MapGet("/incidents/{id:long}/forecast", GetForecast);
        api.MapGet("/detections", ListDetections);
        return api;
    }

    /// <summary>Incidents with a detection in the last <paramref name="hours"/> hours, newest first.</summary>
    private static async Task<Results<Ok<List<IncidentDto>>, ValidationProblem>> ListIncidents(
        PyroLensDbContext db, TimeProvider clock, CancellationToken ct, int hours = 48, IncidentStatus? status = null)
    {
        if (hours is < 1 or > MaxHours)
        {
            return HoursProblem();
        }

        var since = clock.GetUtcNow().AddHours(-hours);
        var query = db.Incidents.AsNoTracking().Where(i => i.LastDetectedAt >= since);
        if (status is { } s)
        {
            query = query.Where(i => i.Status == s);
        }

        var incidents = await query
            .OrderByDescending(i => i.LastDetectedAt)
            .Select(i => new IncidentDto(
                i.Id, i.Status, i.Centroid.Y, i.Centroid.X, i.FirstDetectedAt, i.LastDetectedAt, i.DetectionCount, i.MaxFrpMegawatts))
            .ToListAsync(ct);

        return TypedResults.Ok(incidents);
    }

    private static async Task<Results<Ok<IncidentDetailDto>, NotFound>> GetIncident(long id, PyroLensDbContext db, CancellationToken ct)
    {
        var incident = await db.Incidents.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new IncidentDto(
                i.Id, i.Status, i.Centroid.Y, i.Centroid.X, i.FirstDetectedAt, i.LastDetectedAt, i.DetectionCount, i.MaxFrpMegawatts))
            .SingleOrDefaultAsync(ct);

        if (incident is null)
        {
            return TypedResults.NotFound();
        }

        var detections = await db.Detections.AsNoTracking()
            .Where(d => d.IncidentId == id)
            .OrderBy(d => d.AcquiredAt)
            .Select(ToDto)
            .ToListAsync(ct);

        return TypedResults.Ok(new IncidentDetailDto(incident, detections));
    }

    /// <summary>Latest spread forecast of an incident: one entry per horizon (+1 h, +3 h).</summary>
    private static async Task<Results<Ok<ForecastDto>, NotFound<ProblemDetails>>> GetForecast(long id, PyroLensDbContext db, CancellationToken ct)
    {
        var latest = await db.Forecasts.AsNoTracking()
            .Where(f => f.IncidentId == id)
            .MaxAsync(f => (DateTimeOffset?)f.WeatherFrom, ct);

        if (latest is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "No forecast yet",
                Detail = $"Incident {id} does not exist, is no longer active, or has not been forecast yet. Forecasts run every 30 minutes.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var rows = await db.Forecasts.AsNoTracking()
            .Where(f => f.IncidentId == id && f.WeatherFrom == latest)
            .OrderBy(f => f.HorizonHours)
            .ToListAsync(ct);

        var first = rows[0];
        return TypedResults.Ok(new ForecastDto(
            IncidentId: id,
            IssuedAt: rows.Max(r => r.IssuedAt),
            WeatherFrom: first.WeatherFrom,
            DroughtFactor: first.DroughtFactor,
            FuelLoadTonnesPerHectare: first.FuelLoadTonnesPerHectare,
            Model: first.Model,
            Disclaimer: "Simplified, indicative estimate. Ignores terrain, vegetation maps, firebreaks and suppression. Not for operational use.",
            Attribution: "Weather data by Open-Meteo.com (CC BY 4.0). Fire detections: NASA FIRMS.",
            Horizons: rows.Select(r => new HorizonDto(
                r.HorizonHours,
                new WeatherDto(r.MeanTemperatureC, r.MeanRelativeHumidityPercent, r.MeanWindSpeedKmh, r.MeanWindFromDegrees, r.MaxWindGustKmh),
                r.PeakFfdi,
                r.DangerRating,
                r.PeakRateOfSpreadMetersPerHour,
                r.HeadDistanceMeters,
                r.LengthToBreadth,
                r.SpreadBearingDegrees,
                ToGeoJson(r.SpreadArea))).ToList()));
    }

    private static GeoJsonPolygon ToGeoJson(NetTopologySuite.Geometries.Polygon polygon) =>
        new("Polygon",
        [
            .. new[] { polygon.ExteriorRing }.Concat(polygon.InteriorRings)
                .Select(ring => ring.Coordinates.Select(c => new[] { Math.Round(c.X, 6), Math.Round(c.Y, 6) }).ToArray()),
        ]);

    /// <summary>
    /// Raw detections. The confidence filter defaults to the configured rule (MODIS above 70%, VIIRS nominal/high)
    /// and can be changed per request, e.g. ?modisConfidenceAbove=50&amp;viirsConfidence=low,nominal,high.
    /// Detections outside the area of interest (Greece plus 2 km) are hidden unless includeOutsideArea=true.
    /// </summary>
    private static async Task<Results<Ok<List<DetectionDto>>, ValidationProblem>> ListDetections(
        PyroLensDbContext db,
        TimeProvider clock,
        IOptions<DetectionFilterOptions> defaults,
        CancellationToken ct,
        int hours = 24,
        int? modisConfidenceAbove = null,
        string? viirsConfidence = null,
        bool includeIndustrial = false,
        bool includeOutsideArea = false)
    {
        if (hours is < 1 or > MaxHours)
        {
            return HoursProblem();
        }

        var modis = modisConfidenceAbove ?? defaults.Value.ModisConfidenceAbove;
        if (modis is < 0 or > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["modisConfidenceAbove"] = ["Must be between 0 and 100."],
            });
        }

        IReadOnlyList<ConfidenceClass> viirs;
        try
        {
            viirs = DetectionQueries.ParseViirsClasses(
                viirsConfidence?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? (IEnumerable<string>)defaults.Value.ViirsConfidence);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["viirsConfidence"] = [ex.Message] });
        }

        var since = clock.GetUtcNow().AddHours(-hours);
        var query = db.Detections.AsNoTracking()
            .Where(d => d.AcquiredAt >= since)
            .WherePassesConfidence(modis, viirs)
            .WhereInsideAreaOfInterest(defaults.Value.RequireAreaOfInterest && !includeOutsideArea);

        if (!includeIndustrial)
        {
            query = query.Where(d => !d.LikelyIndustrial);
        }

        var detections = await query.OrderByDescending(d => d.AcquiredAt).Select(ToDto).ToListAsync(ct);
        return TypedResults.Ok(detections);
    }

    private static readonly System.Linq.Expressions.Expression<Func<FireDetection, DetectionDto>> ToDto = d => new DetectionDto(
        d.Id, d.Source, d.Instrument, d.Latitude, d.Longitude, d.AcquiredAt, d.ConfidenceRaw, d.FrpMegawatts, d.LikelyIndustrial, d.AreaCode, d.IncidentId);

    private static ValidationProblem HoursProblem() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["hours"] = [$"Must be between 1 and {MaxHours}."] });
}
