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
builder.Services.AddHostedService<DatabaseMigrationService>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);

// Dispatcher actions: 30 a minute per client, which also slows down guessing the operator key.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(OperationsEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        ClientAddress(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

// The map (web/, built into wwwroot by the Dockerfile) is served from the same origin as the API.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
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
});

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

var api = app.MapGroup("/api");
api.MapPyroLensEndpoints();
api.MapOperationsEndpoints();

app.Run();

// Behind Render's proxy every request comes from the proxy; the first X-Forwarded-For entry is the client.
static string ClientAddress(HttpContext http) =>
    http.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim() is { Length: > 0 } forwarded
        ? forwarded
        : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;
