using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using PyroLens.Core.Incidents;
using PyroLens.Core.Resources;
using PyroLens.Infrastructure.Operations;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Api;

public sealed class OperatorOptions
{
    public const string Section = "Operator";

    /// <summary>Shared secret for dispatcher actions. Unset: the server is read-only.</summary>
    public string? Key { get; set; }
}

public sealed record StatusChangeRequest(IncidentStatus From, IncidentStatus To, string? Note);

public sealed record ResourceActionRequest(long IncidentId);

public sealed record AirspaceRequest(AirspaceLevel Level, double RadiusMeters, int DurationMinutes, DateTimeOffset? StartsAt);

public sealed record ResourceDto(
    long Id, string Callsign, ResourceKind Kind, string BaseName, double Latitude, double Longitude,
    ResourceStatus Status, long? IncidentId, DateTimeOffset? ReservedUntil);

public sealed record SuggestionDto(ResourceDto Resource, double DistanceMeters, int EtaMinutes);

public sealed record EventDto(
    long Id, DateTimeOffset OccurredAt, IncidentEventType Type, string Actor,
    IncidentStatus? FromStatus, IncidentStatus? ToStatus, string? Note, JsonElement Details);

public sealed record AirspaceDto(
    long Id, long IncidentId, AirspaceLevel Level, double RadiusMeters,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string ReservedBy, Geometry Area);

public static class OperationsEndpoints
{
    public const string RateLimitPolicy = "operator";
    private const int MaxActorLength = 40;

    public static RouteGroupBuilder MapOperationsEndpoints(this RouteGroupBuilder api)
    {
        // Reads: open to everyone.
        api.MapGet("/incidents/{id:long}/events", Timeline);
        api.MapGet("/resources", ListResources);
        api.MapGet("/incidents/{id:long}/resources/suggestions", Suggestions);
        api.MapGet("/airspace", ListAirspace);

        // Writes: operator key and rate limit.
        var ops = api.MapGroup("").AddEndpointFilter<OperatorKeyFilter>().RequireRateLimiting(RateLimitPolicy);
        ops.MapGet("/operator", () => TypedResults.NoContent());
        ops.MapPost("/incidents/{id:long}/status", ChangeStatus);
        ops.MapPost("/resources/{id:long}/reserve", Reserve);
        ops.MapPost("/resources/{id:long}/assign", Assign);
        ops.MapPost("/resources/{id:long}/release", Release);
        ops.MapPost("/incidents/{id:long}/airspace", ReserveAirspace);
        ops.MapDelete("/airspace/{id:long}", CancelAirspace);
        return api;
    }

    private static async Task<IResult> Timeline(long id, IncidentOperations incidents, CancellationToken ct) =>
        TypedResults.Ok((await incidents.TimelineAsync(id, ct)).Select(ToDto).ToList());

    private static async Task<IResult> ListResources(ResourceOperations resources, CancellationToken ct) =>
        TypedResults.Ok((await resources.ListAsync(ct)).Select(ToDto).ToList());

    private static async Task<IResult> Suggestions(long id, ResourceOperations resources, CancellationToken ct, int limit = 6) =>
        ToResult(await resources.SuggestAsync(id, Math.Clamp(limit, 1, 20), ct),
            list => list.Select(s => new SuggestionDto(ToDto(s.Resource), Math.Round(s.DistanceMeters), s.EtaMinutes)).ToList());

    private static async Task<IResult> ListAirspace(AirspaceOperations airspace, CancellationToken ct, long? incidentId = null) =>
        TypedResults.Ok((await airspace.ListActiveAsync(incidentId, ct)).Select(ToDto).ToList());

    private static async Task<IResult> ChangeStatus(long id, StatusChangeRequest body, HttpContext http, IncidentOperations incidents, CancellationToken ct)
    {
        if (body.Note is { Length: > 500 })
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "Note too long", "Keep the note under 500 characters.");
        }

        return ToResult(await incidents.ChangeStatusAsync(id, body.From, body.To, Actor(http), body.Note, ct), i => new { i.Id, i.Status, i.UpdatedAt });
    }

    private static async Task<IResult> Reserve(long id, ResourceActionRequest body, HttpContext http, ResourceOperations resources, CancellationToken ct) =>
        ToResult(await resources.ReserveAsync(id, body.IncidentId, Actor(http), ct), ToDto);

    private static async Task<IResult> Assign(long id, ResourceActionRequest body, HttpContext http, ResourceOperations resources, CancellationToken ct) =>
        ToResult(await resources.AssignAsync(id, body.IncidentId, Actor(http), ct), ToDto);

    private static async Task<IResult> Release(long id, ResourceActionRequest body, HttpContext http, ResourceOperations resources, CancellationToken ct) =>
        ToResult(await resources.ReleaseAsync(id, body.IncidentId, Actor(http), ct), ToDto);

    private static async Task<IResult> ReserveAirspace(
        long id, AirspaceRequest body, HttpContext http, AirspaceOperations airspace, TimeProvider clock, CancellationToken ct)
    {
        var starts = body.StartsAt ?? clock.GetUtcNow();
        var result = await airspace.ReserveAsync(id, body.Level, body.RadiusMeters, starts, starts.AddMinutes(body.DurationMinutes), Actor(http), ct);
        return ToResult(result, ToDto);
    }

    private static async Task<IResult> CancelAirspace(long id, HttpContext http, AirspaceOperations airspace, CancellationToken ct) =>
        ToResult(await airspace.CancelAsync(id, Actor(http), ct), ToDto);

    /// <summary>Who acted, for the timeline: the optional X-Operator-Name header, else "operator".</summary>
    private static string Actor(HttpContext http)
    {
        var name = new string(http.Request.Headers["X-Operator-Name"].ToString()
            .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.').ToArray()).Trim();
        return name.Length == 0 ? "operator" : name[..Math.Min(name.Length, MaxActorLength)];
    }

    private static IResult ToResult<T, TDto>(OperationResult<T> result, Func<T, TDto> map) => result.Outcome switch
    {
        Outcome.Ok => TypedResults.Ok(map(result.Value!)),
        Outcome.NotFound => Problem(StatusCodes.Status404NotFound, "Not found", result.Message),
        Outcome.Conflict => Problem(StatusCodes.Status409Conflict, "Conflict", result.Message),
        _ => Problem(StatusCodes.Status422UnprocessableEntity, "Not allowed", result.Message),
    };

    private static IResult Problem(int status, string title, string? detail) =>
        TypedResults.Problem(detail: detail, statusCode: status, title: title);

    private static ResourceDto ToDto(Resource r) => new(
        r.Id, r.Callsign, r.Kind, r.BaseName, r.BaseLocation.Y, r.BaseLocation.X, r.Status, r.IncidentId, r.ReservedUntil);

    private static EventDto ToDto(IncidentEvent e) => new(
        e.Id, e.OccurredAt, e.Type, e.Actor, e.FromStatus, e.ToStatus, e.Note, JsonDocument.Parse(e.Details).RootElement.Clone());

    private static AirspaceDto ToDto(AirspaceReservation a) => new(
        a.Id, a.IncidentId, a.Level, a.RadiusMeters,
        new DateTimeOffset(a.During.LowerBound, TimeSpan.Zero), new DateTimeOffset(a.During.UpperBound, TimeSpan.Zero),
        a.ReservedBy, a.Area);
}

/// <summary>Allows dispatcher actions only with the configured operator key (compared in constant time).</summary>
public sealed class OperatorKeyFilter(IOptions<OperatorOptions> options) : IEndpointFilter
{
    public const string Header = "X-Operator-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var key = options.Value.Key;
        if (string.IsNullOrEmpty(key))
        {
            return TypedResults.Problem(
                title: "Operator actions are disabled",
                detail: "This server has no operator key configured, so it is read-only.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var given = context.HttpContext.Request.Headers[Header].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key)))
        {
            return TypedResults.Problem(
                title: "Operator key required",
                detail: $"Send the operator key in the {Header} header.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }
}
