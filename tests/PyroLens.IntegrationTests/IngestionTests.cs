using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PyroLens.Core.Detections;
using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.IntegrationTests;

/// <summary>
/// Runs the real SQL against PostGIS in Docker, using the synthetic sample (see samples/).
/// The clock is fixed at 06:00 UTC, so the sample's newest day (00:10–01:27 UTC) is inside the 24 h window
/// and the previous days are not.
/// </summary>
[Collection(PostgisCollection.Name)]
public sealed class IngestionTests(PostgisFixture postgis)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migrations_can_run_twice()
    {
        await using var services = await postgis.CreateServicesAsync(Now);

        await services.MigrateDatabaseAsync();
    }

    [Fact]
    public async Task Ingestion_is_idempotent()
    {
        await using var services = await postgis.CreateServicesAsync(Now);

        var first = await RunCycleAsync(services);
        var second = await RunCycleAsync(services);

        Assert.Equal(18, first.Inserted);
        Assert.Equal(17, first.InsideArea);
        Assert.Equal(18, second.Received);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Clustering.NewIncidents);
    }

    [Fact]
    public async Task Sample_produces_three_incidents_and_filters_the_rest()
    {
        await using var services = await postgis.CreateServicesAsync(Now);

        var result = await RunCycleAsync(services);

        Assert.Equal(3, result.Clustering.NewIncidents);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PyroLensDbContext>();

        var incidents = await db.Incidents.OrderByDescending(i => i.DetectionCount).ToListAsync();
        Assert.Equal([6, 3, 2], incidents.Select(i => i.DetectionCount));

        // Parnitha: 5 VIIRS + 1 MODIS pixel, centroid close to the pixels.
        var parnitha = incidents[0];
        Assert.InRange(parnitha.Centroid.Y, 38.16, 38.18);
        Assert.InRange(parnitha.Centroid.X, 23.71, 23.73);
        Assert.Equal(42.0, parnitha.MaxFrpMegawatts);

        // Low-confidence pixels are stored but never become incidents.
        var lowConfidence = await db.Detections
            .Where(d => d.ConfidenceClass == ConfidenceClass.Low || d.ConfidencePercent == 40)
            .ToListAsync();
        Assert.Equal(2, lowConfidence.Count);
        Assert.All(lowConfidence, d => Assert.Null(d.IncidentId));

        // Inland Turkey: inside the fetch bounding box, outside Greece.
        var turkey = await db.Detections.SingleAsync(d => d.Longitude > 28.5);
        Assert.Null(turkey.AreaCode);
        Assert.Null(turkey.IncidentId);
        Assert.Equal(17, await db.Detections.CountAsync(d => d.AreaCode == "GRC"));
    }

    [Fact]
    public async Task Area_of_interest_keeps_coastal_pixels_and_drops_neighbours()
    {
        await using var services = await postgis.CreateServicesAsync(Now);
        await using var scope = services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<DetectionWriter>();
        var clusterer = scope.ServiceProvider.GetRequiredService<IncidentClusterer>();

        // Distances to the Greek boundary were measured in EPSG:2100 against the stored polygon.
        var result = await writer.WriteAsync("TEST",
        [
            Viirs(37.925, 23.620, Now.AddHours(-1)), // sea off Piraeus, 1.1 km from the coast -> inside the 2 km buffer
            Viirs(36.455, 28.240, Now.AddHours(-1)), // sea off Rhodes, 1.0 km from the coast -> inside
            Viirs(36.500, 21.000, Now.AddHours(-1)), // open Ionian sea, 68 km -> outside
            Viirs(41.000, 26.400, Now.AddHours(-1)), // Turkish side of the Evros, 2.8 km from the border -> outside
        ], CancellationToken.None);

        Assert.Equal(2, result.InsideArea);

        var clustering = await clusterer.RunAsync(CancellationToken.None);
        Assert.Equal(2, clustering.Candidates);
        Assert.Equal(2, clustering.NewIncidents);
    }

    [Fact]
    public async Task Spot_seen_on_consecutive_days_is_flagged_industrial()
    {
        await using var services = await postgis.CreateServicesAsync(Now);

        var result = await RunCycleAsync(services);

        Assert.Equal(1, result.FlaggedIndustrial);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PyroLensDbContext>();
        var refinery = await db.Detections
            .Where(d => d.Latitude > 38.03 && d.Latitude < 38.05 && d.Longitude > 23.58 && d.Longitude < 23.60)
            .OrderBy(d => d.AcquiredAt)
            .ToListAsync();

        Assert.Equal(4, refinery.Count);
        Assert.Equal([false, false, false, true], refinery.Select(d => d.LikelyIndustrial));
        Assert.All(refinery, d => Assert.Null(d.IncidentId));
    }

    [Fact]
    public async Task New_detection_near_open_incident_joins_it()
    {
        await using var services = await postgis.CreateServicesAsync(Now);
        await RunCycleAsync(services);

        await using var scope = services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<DetectionWriter>();
        var clusterer = scope.ServiceProvider.GetRequiredService<IncidentClusterer>();

        // ~300 m from the nearest Parnitha pixel, an hour ago.
        await writer.WriteAsync("TEST", [Viirs(38.1760, 23.7260, Now.AddHours(-1))], CancellationToken.None);
        var clustering = await clusterer.RunAsync(CancellationToken.None);

        Assert.Equal(1, clustering.AttachedToExisting);
        Assert.Equal(0, clustering.NewIncidents);

        var db = scope.ServiceProvider.GetRequiredService<PyroLensDbContext>();
        var parnitha = await db.Incidents.AsNoTracking().OrderByDescending(i => i.DetectionCount).FirstAsync();
        Assert.Equal(7, parnitha.DetectionCount);
        Assert.Equal(Now.AddHours(-1), parnitha.LastDetectedAt);
    }

    [Fact]
    public async Task Distant_detections_start_separate_incidents()
    {
        await using var services = await postgis.CreateServicesAsync(Now);
        await using var scope = services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<DetectionWriter>();
        var clusterer = scope.ServiceProvider.GetRequiredService<IncidentClusterer>();

        // Two pixels 300 m apart and one ~11 km away, all inland Peloponnese.
        await writer.WriteAsync("TEST",
        [
            Viirs(37.9000, 22.9000, Now.AddHours(-2)),
            Viirs(37.9027, 22.9000, Now.AddHours(-2)),
            Viirs(37.8000, 22.9000, Now.AddHours(-2)),
        ], CancellationToken.None);

        var clustering = await clusterer.RunAsync(CancellationToken.None);

        Assert.Equal(3, clustering.Candidates);
        Assert.Equal(2, clustering.NewIncidents);
    }

    private static async Task<IngestionCycleResult> RunCycleAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IngestionService>().RunOnceAsync(CancellationToken.None);
    }

    private static RawDetection Viirs(double lat, double lon, DateTimeOffset at) =>
        new(lat, lon, at, Instrument.Viirs, "N", "h", ConfidenceClass.High, null, 330, 10, false);
}
