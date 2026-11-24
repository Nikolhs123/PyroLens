using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NetTopologySuite.IO.Converters;
using PyroLens.Api;
using PyroLens.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPyroLensCore(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    // Geometries (footprints, spread areas) are written as GeoJSON.
    o.SerializerOptions.Converters.Add(new GeoJsonConverterFactory());
});
builder.Services.AddProblemDetails();
builder.Services.AddHostedService<DatabaseMigrationService>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

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

app.MapGroup("/api").MapPyroLensEndpoints();

app.Run();

public partial class Program;
