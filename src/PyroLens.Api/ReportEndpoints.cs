using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PyroLens.Core.Forecasting;
using PyroLens.Core.Incidents;
using PyroLens.Core.Reports;
using PyroLens.Infrastructure.Operations;
using PyroLens.Infrastructure.Persistence;
using PyroLens.Infrastructure.Reports;

namespace PyroLens.Api;

/// <param name="Website">A field people never see (hidden on the form). Bots fill in every field, so a value here means spam.</param>
public sealed record ReportRequest(double Latitude, double Longitude, ReportKind Kind, string? Note, string? Website);

public sealed record ReportReceiptDto(long? Id, string Message);

/// <param name="Note">Only for dispatchers with the operator key: it is free text from anyone.</param>
/// <param name="NearestIncidentId">The open fire nearest to the report, if one is within 3 km.</param>
public sealed record ReportDto(
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

public sealed record ReviewRequest(ReportStatus Status);

/// <summary>What the public map shows of a fire: where, how sure, how dangerous. Nothing operational.</summary>
public sealed record PublicFireDto(long Id, IncidentStatus Status, double Latitude, double Longitude, FireDangerRating? DangerRating, DateTimeOffset LastSeenAt);

public static class ReportEndpoints
{
    public const string SubmitPolicy = "public-reports";
    public const string PublicCachePolicy = "public-map";
    private const int MaxHours = 168;
    private const string Thanks = "Thank you. Dispatchers can see your report now. If anyone is in danger, call 112.";

    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder api)
    {
        // For the public page: the map is served from a 30-second cache, so a crowd of visitors costs one query.
        api.MapGet("/public/fires", PublicFires).CacheOutput(PublicCachePolicy);
        api.MapPost("/public/reports", Submit).RequireRateLimiting(SubmitPolicy);

        // For dispatchers.
        api.MapGet("/reports", List);
        api.MapPost("/reports/{id:long}/review", Review)
            .AddEndpointFilter<OperatorKeyFilter>()
            .RequireRateLimiting(OperationsEndpoints.RateLimitPolicy);
        return api;
    }

    private static async Task<IResult> PublicFires(HttpContext http, PyroLensDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddHours(-48);
        var fires = await Endpoints.OnBoard(db.Incidents.AsNoTracking(), since)
            .OrderByDescending(i => i.LastDetectedAt)
            .Select(i => new { i.Id, i.Status, i.Centroid, i.LastDetectedAt })
            .ToListAsync(ct);
        var ratings = await Endpoints.LatestForecastSummariesAsync(db, fires.Select(f => f.Id).ToList(), ct);

        http.Response.Headers.CacheControl = "public, max-age=30";
        // About 100 m of precision: enough to see where a fire is, no more than the satellites give.
        return TypedResults.Ok(fires.Select(f => new PublicFireDto(
            f.Id, f.Status, Math.Round(f.Centroid.Y, 3), Math.Round(f.Centroid.X, 3),
            ratings.GetValueOrDefault(f.Id)?.DangerRating, f.LastDetectedAt)).ToList());
    }

    private static async Task<IResult> Submit(ReportRequest body, ReportService reports, CancellationToken ct)
    {
        // Same answer for bots as for people, so they cannot tell they were filtered out.
        if (!string.IsNullOrEmpty(body.Website))
        {
            return TypedResults.Json(new ReportReceiptDto(null, Thanks), statusCode: StatusCodes.Status201Created);
        }

        var result = await reports.SubmitAsync(body.Latitude, body.Longitude, body.Kind, body.Note, ct);
        return result.Outcome == Outcome.Ok
            ? TypedResults.Json(new ReportReceiptDto(result.Value!.Id, Thanks), statusCode: StatusCodes.Status201Created)
            : TypedResults.Problem(detail: result.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Report not accepted");
    }

    private static async Task<IResult> List(
        HttpContext http, ReportService reports, IOptions<OperatorOptions> operatorOptions, TimeProvider clock, CancellationToken ct,
        int hours = 24, bool includeDismissed = false)
    {
        if (hours is < 1 or > MaxHours)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["hours"] = [$"Must be between 1 and {MaxHours}."] });
        }

        var isOperator = OperatorKeyFilter.IsValid(http, operatorOptions.Value.Key);
        var list = await reports.ListAsync(clock.GetUtcNow().AddHours(-hours), includeDismissed, ct);
        return TypedResults.Ok(list.Select(r => ToDto(r, isOperator)).ToList());
    }

    private static async Task<IResult> Review(long id, ReviewRequest body, HttpContext http, ReportService reports, CancellationToken ct)
    {
        var result = await reports.ReviewAsync(id, body.Status, OperationsEndpoints.Actor(http), ct);
        return result.Outcome switch
        {
            Outcome.Ok => TypedResults.Ok(ToDto(result.Value!, withNote: true)),
            Outcome.NotFound => TypedResults.Problem(detail: result.Message, statusCode: StatusCodes.Status404NotFound, title: "Not found"),
            Outcome.Conflict => TypedResults.Problem(detail: result.Message, statusCode: StatusCodes.Status409Conflict, title: "Conflict"),
            _ => TypedResults.Problem(detail: result.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Not allowed"),
        };
    }

    private static ReportDto ToDto(ReportView r, bool withNote) => new(
        r.Id, r.Latitude, r.Longitude, r.Kind, withNote ? r.Note : null, r.SubmittedAt, r.Status, r.ReviewedBy, r.ReviewedAt,
        r.NearestIncidentId, r.NearestIncidentMeters);
}
