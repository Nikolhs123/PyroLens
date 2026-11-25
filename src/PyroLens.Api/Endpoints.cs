using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using PyroLens.Core.Detections;
using PyroLens.Core.Forecasting;
using PyroLens.Core.Incidents;
using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Jobs;
using PyroLens.Infrastructure.Operations;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Api;

/// <summary>Latest +3 h forecast of an incident, enough to colour it on a map.</summary>
public sealed record ForecastSummaryDto(
    DateTimeOffset WeatherFrom,
    FireDangerRating DangerRating,
    double PeakFfdi,
    double HeadDistanceMeters,
    double SpreadBearingDegrees);

/// <param name="Footprint">GeoJSON geometry of the detections' convex hull: a Point, LineString or Polygon.</param>
/// <param name="NextStatuses">Statuses the incident may move to, from the lifecycle rules, so clients need no copy of them.</param>
/// <param name="AcceptsResources">Whether units and airspace can be committed in the current status.</param>
/// <param name="Attention">Why the fire needs a dispatcher's attention right now (escalation rules); empty when it does not.</param>
public sealed record IncidentDto(
    long Id,
    IncidentStatus Status,
    double Latitude,
    double Longitude,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastDetectedAt,
    int DetectionCount,
    double? MaxFrpMegawatts,
    Geometry? Footprint,
    ForecastSummaryDto? Forecast,
    IReadOnlyList<IncidentStatus> NextStatuses,
    bool AcceptsResources,
    IReadOnlyList<Attention> Attention);

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

/// <param name="SpreadArea">GeoJSON polygon of where the fire could be by the end of the horizon.</param>
public sealed record HorizonDto(
    int HorizonHours,
    WeatherDto Weather,
    double PeakFfdi,
    FireDangerRating DangerRating,
    double PeakRateOfSpreadMetersPerHour,
    double HeadDistanceMeters,
    double LengthToBreadth,
    double SpreadBearingDegrees,
    Geometry SpreadArea);

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

/// <param name="LastSucceededAt">When the job last finished without failing: how fresh the data is.</param>
/// <param name="LastStartedBy">"worker", "schedule" (GitHub Actions) or "website" (the site itself, on a visit).</param>
public sealed record JobStatusDto(DateTimeOffset? LastSucceededAt, DateTimeOffset LastStartedAt, string LastStartedBy, bool Running);

/// <param name="Ingestion">The fire-data job; null until it has run once.</param>
/// <param name="Forecast">The weather and forecast job; null until it has run once.</param>
public sealed record StatusDto(
    DateTimeOffset? LastDetectionIngestedAt,
    DateTimeOffset? LastForecastIssuedAt,
    int ActiveIncidents,
    JobStatusDto? Ingestion,
    JobStatusDto? Forecast);

public static class Endpoints
{
    private const int MaxHours = 168;
    private const int ActiveHours = 48;

    public static RouteGroupBuilder MapPyroLensEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/status", GetStatus);
        api.MapGet("/incidents", ListIncidents);
        api.MapGet("/incidents/{id:long}", GetIncident);
        api.MapGet("/incidents/{id:long}/forecast", GetForecast);
        api.MapGet("/detections", ListDetections);
        return api;
    }

    /// <summary>When data last arrived and whether a job is fetching more right now, so a client can show how fresh the map is.</summary>
    private static async Task<Ok<StatusDto>> GetStatus(PyroLensDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var since = now.AddHours(-ActiveHours);
        var runs = await db.JobRuns.AsNoTracking().ToDictionaryAsync(r => r.Job, ct);

        JobStatusDto? Describe(BackgroundJob job) =>
            runs.TryGetValue(JobRunner.Name(job), out var r)
                ? new JobStatusDto(r.LastSucceededAt, r.LastStartedAt, r.LastStartedBy, r.IsRunning(now, JobRunner.StuckAfter))
                : null;

        return TypedResults.Ok(new StatusDto(
            await db.Detections.MaxAsync(d => (DateTimeOffset?)d.IngestedAt, ct),
            await db.Forecasts.MaxAsync(f => (DateTimeOffset?)f.IssuedAt, ct),
            await OnBoard(db.Incidents, since).CountAsync(ct),
            Describe(BackgroundJob.Ingestion),
            Describe(BackgroundJob.Forecast)));
    }

    /// <summary>
    /// The board: incidents that are not closed and either were detected in the last <paramref name="hours"/> hours or
    /// are being worked on (confirmed, active, contained), newest first, with footprint and latest forecast.
    /// <c>?status=closed</c> lists recently closed incidents instead.
    /// </summary>
    private static async Task<Results<Ok<List<IncidentDto>>, ValidationProblem>> ListIncidents(
        PyroLensDbContext db, AttentionService attention, TimeProvider clock, CancellationToken ct, int hours = ActiveHours, IncidentStatus? status = null)
    {
        if (hours is < 1 or > MaxHours)
        {
            return HoursProblem();
        }

        var since = clock.GetUtcNow().AddHours(-hours);
        var query = status == IncidentStatus.Closed
            ? db.Incidents.AsNoTracking().Where(i => i.Status == IncidentStatus.Closed && i.UpdatedAt >= since)
            : OnBoard(db.Incidents.AsNoTracking(), since);
        if (status is { } s and not IncidentStatus.Closed)
        {
            query = query.Where(i => i.Status == s);
        }

        var incidents = await query.OrderByDescending(i => i.LastDetectedAt).ToListAsync(ct);
        var ids = incidents.Select(i => i.Id).ToList();
        var forecasts = await LatestForecastSummariesAsync(db, ids, ct);
        var flags = await attention.EvaluateAsync(ids, ct);

        return TypedResults.Ok(incidents.Select(i => ToDto(i, forecasts.GetValueOrDefault(i.Id), flags.GetValueOrDefault(i.Id))).ToList());
    }

    private static async Task<Results<Ok<IncidentDetailDto>, NotFound>> GetIncident(
        long id, PyroLensDbContext db, AttentionService attention, CancellationToken ct)
    {
        var incident = await db.Incidents.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null)
        {
            return TypedResults.NotFound();
        }

        var forecasts = await LatestForecastSummariesAsync(db, [id], ct);
        var flags = await attention.EvaluateAsync([id], ct);
        var detections = await db.Detections.AsNoTracking()
            .Where(d => d.IncidentId == id)
            .OrderBy(d => d.AcquiredAt)
            .Select(ToDetectionDto)
            .ToListAsync(ct);

        return TypedResults.Ok(new IncidentDetailDto(ToDto(incident, forecasts.GetValueOrDefault(id), flags.GetValueOrDefault(id)), detections));
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
                r.SpreadArea)).ToList()));
    }

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

        var detections = await query.OrderByDescending(d => d.AcquiredAt).Select(ToDetectionDto).ToListAsync(ct);
        return TypedResults.Ok(detections);
    }

    /// <summary>Latest longest-horizon forecast per incident, in one query.</summary>
    internal static async Task<Dictionary<long, ForecastSummaryDto>> LatestForecastSummariesAsync(
        PyroLensDbContext db, List<long> incidentIds, CancellationToken ct)
    {
        if (incidentIds.Count == 0)
        {
            return [];
        }

        var horizon = (short)ForecastOptions.Horizons.Max();
        var latest = await db.Forecasts.AsNoTracking()
            .Where(f => f.HorizonHours == horizon && incidentIds.Contains(f.IncidentId))
            .GroupBy(f => f.IncidentId)
            .Select(g => g.OrderByDescending(f => f.WeatherFrom)
                .Select(f => new { f.IncidentId, f.WeatherFrom, f.DangerRating, f.PeakFfdi, f.HeadDistanceMeters, f.SpreadBearingDegrees })
                .First())
            .ToListAsync(ct);

        return latest.ToDictionary(
            f => f.IncidentId,
            f => new ForecastSummaryDto(f.WeatherFrom, f.DangerRating, f.PeakFfdi, f.HeadDistanceMeters, f.SpreadBearingDegrees));
    }

    /// <summary>Not closed, and recently detected or managed by a dispatcher.</summary>
    internal static IQueryable<Incident> OnBoard(IQueryable<Incident> incidents, DateTimeOffset since) =>
        incidents.Where(i => i.Status != IncidentStatus.Closed &&
                             (i.LastDetectedAt >= since ||
                              i.Status == IncidentStatus.Confirmed || i.Status == IncidentStatus.Active || i.Status == IncidentStatus.Contained));

    private static IncidentDto ToDto(Incident i, ForecastSummaryDto? forecast, IReadOnlyList<Attention>? attention) => new(
        i.Id, i.Status, i.Centroid.Y, i.Centroid.X, i.FirstDetectedAt, i.LastDetectedAt, i.DetectionCount, i.MaxFrpMegawatts,
        i.Footprint, forecast, IncidentLifecycle.NextStatuses(i.Status), IncidentLifecycle.AcceptsResources(i.Status), attention ?? []);

    private static readonly System.Linq.Expressions.Expression<Func<FireDetection, DetectionDto>> ToDetectionDto = d => new DetectionDto(
        d.Id, d.Source, d.Instrument, d.Latitude, d.Longitude, d.AcquiredAt, d.ConfidenceRaw, d.FrpMegawatts, d.LikelyIndustrial, d.AreaCode, d.IncidentId);

    private static ValidationProblem HoursProblem() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["hours"] = [$"Must be between 1 and {MaxHours}."] });
}
