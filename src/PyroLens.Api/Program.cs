using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NetTopologySuite.IO.Converters;
using PyroLens.Api;
using PyroLens.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPyroLensCore(builder.Configuration);
builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection(OperatorOptions.Section));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    // Geometries (footprints, spread areas, airspace) are written as GeoJSON.
    o.SerializerOptions.Converters.Add(new GeoJsonConverterFactory());
});
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<DatabaseReadiness>();
builder.Services.AddHostedService<DatabaseMigrationService>();
var liveRefreshProblem = AddLiveRefresh(builder);
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);

// Dispatcher actions: 30 a minute per client, which also slows down guessing the operator key.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(OperationsEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        ClientAddress(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // Reports from the public: 3 per 10 minutes per client is plenty for a person and stops a script flooding the board.
    options.AddPolicy(ReportEndpoints.SubmitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        ClientAddress(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
});

// The public map is the page a crowd opens during a big fire; one cached answer every 30 seconds serves them all.
builder.Services.AddOutputCache(options =>
    options.AddPolicy(ReportEndpoints.PublicCachePolicy, policy => policy.Expire(TimeSpan.FromSeconds(30))));

var app = builder.Build();

if (liveRefreshProblem is not null)
{
    app.Logger.LogWarning("Live refresh is off: {Problem}", liveRefreshProblem);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseOutputCache();

// The map (web/, built into wwwroot by the Dockerfile) is served from the same origin as the API.
var staticFiles = new StaticFileOptions
{
    // index.html is always revalidated so a deploy shows up at once; the files in /assets have a content hash
    // in their names, so they can be cached for a year.
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl =
            context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "no-cache"
            : context.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable"
            : "public, max-age=3600";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);

// The public page is the same web app at another address; the app reads the path and shows the citizen view.
app.MapFallbackToFile("/public/{**rest}", "index.html", staticFiles);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

var api = app.MapGroup("/api");
api.MapPyroLensEndpoints();
api.MapOperationsEndpoints();
api.MapReportEndpoints();

app.Run();

// The site runs the fire and weather jobs itself while awake (see LiveRefreshService). Only with real FIRMS data:
// in File mode it would load the synthetic sample into the live database. Returns why it is off, if it was asked for.
static string? AddLiveRefresh(WebApplicationBuilder builder)
{
    var refresh = builder.Configuration.GetSection(LiveRefreshOptions.Section).Get<LiveRefreshOptions>() ?? new LiveRefreshOptions();
    if (!refresh.Enabled)
    {
        return null;
    }

    var firms = builder.Configuration.GetSection(FirmsOptions.Section).Get<FirmsOptions>() ?? new FirmsOptions();
    if (firms.Mode != FirmsMode.Api || string.IsNullOrWhiteSpace(firms.MapKey))
    {
        return "LiveRefresh__Enabled is true, but it also needs Firms__Mode=Api and a Firms__MapKey.";
    }

    builder.Services.AddPyroLensIngestion(builder.Configuration);
    builder.Services.AddPyroLensForecasting(builder.Configuration);
    builder.Services.Configure<LiveRefreshOptions>(builder.Configuration.GetSection(LiveRefreshOptions.Section));
    builder.Services.AddHostedService<LiveRefreshService>();
    return null;
}

// Behind Render's proxy every request comes from the proxy; the first X-Forwarded-For entry is the client.
static string ClientAddress(HttpContext http) =>
    http.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim() is { Length: > 0 } forwarded
        ? forwarded
        : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;
