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
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

await app.Services.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The map (web/, built into wwwroot by the Dockerfile) is served from the same origin as the API.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapGroup("/api").MapPyroLensEndpoints();

app.Run();

public partial class Program;
