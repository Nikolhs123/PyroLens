using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using PyroLens.Core.Forecasting;
using PyroLens.Infrastructure.Forecasting;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Persistence;
using PyroLens.Infrastructure.Weather;

namespace PyroLens.IntegrationTests;

/// <summary>Forecasts for the three sample incidents, with fixed weather instead of Open-Meteo.</summary>
[Collection(PostgisCollection.Name)]
public sealed class ForecastTests(PostgisFixture postgis)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Writes_one_and_three_hour_forecasts_for_each_active_incident()
    {
        await using var services = await CreateAsync();
        await IngestSampleAsync(services);

        var result = await RunForecastAsync(services);

        Assert.Equal(3, result.ActiveIncidents);
        Assert.Equal(6, result.ForecastsWritten);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PyroLensDbContext>();
        var forecasts = await db.Forecasts.AsNoTracking().ToListAsync();

        Assert.Equal(6, forecasts.Count);
        // Drought factor 8 (default): 2·exp(−0.45 + 0.987·ln8 − 0.0345·18 + 0.0338·34 + 0.0234·30) ≈ 34.
        Assert.All(forecasts, f => Assert.Equal(34.0, f.PeakFfdi, 0));
        Assert.All(forecasts, f => Assert.Equal(FireDangerRating.VeryHigh, f.DangerRating));
        Assert.All(forecasts, f => Assert.Equal(180, f.SpreadBearingDegrees, 6));
        Assert.All(forecasts, f => Assert.True(f.SpreadArea.IsValid));
    }

    [Fact]
    public async Task Spread_area_covers_the_incident_and_reaches_downwind()
    {
        await using var services = await CreateAsync();
        await IngestSampleAsync(services);
        await RunForecastAsync(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PyroLensDbContext>();
        var parnitha = await db.Incidents.AsNoTracking().OrderByDescending(i => i.DetectionCount).FirstAsync();
        var forecasts = await db.Forecasts.AsNoTracking()
            .Where(f => f.IncidentId == parnitha.Id)
            .OrderBy(f => f.HorizonHours)
            .ToListAsync();

        var oneHour = forecasts[0];
        var threeHours = forecasts[1];

        Assert.True(oneHour.SpreadArea.Covers(parnitha.Footprint!));
        Assert.True(threeHours.SpreadArea.Covers(oneHour.SpreadArea));
        Assert.Equal(3 * oneHour.HeadDistanceMeters, threeHours.HeadDistanceMeters, 6);

        // North wind: the head runs the full distance south of the centre; northwards the area
        // only reaches the edge of the current footprint (the backing spread is tiny).
        var envelope = threeHours.SpreadArea.EnvelopeInternal;
        var southReachMeters = (parnitha.Centroid.Y - envelope.MinY) * 111_320;
        Assert.Equal(threeHours.HeadDistanceMeters, southReachMeters, threeHours.HeadDistanceMeters * 0.01);
        Assert.Equal(parnitha.Footprint!.EnvelopeInternal.MaxY, envelope.MaxY, 6);
    }

    [Fact]
    public async Task Rerunning_in_the_same_hour_updates_instead_of_duplicating()
    {
        await using var services = await CreateAsync();
        await IngestSampleAsync(services);

        await RunForecastAsync(services);
        await RunForecastAsync(services);

        await using var scope = services.CreateAsyncScope();
        Assert.Equal(6, await scope.ServiceProvider.GetRequiredService<PyroLensDbContext>().Forecasts.CountAsync());
    }

    [Fact]
    public async Task No_active_incidents_means_no_weather_request()
    {
        var weather = new FixedWeather();
        await using var services = await postgis.CreateServicesAsync(Now, s => s.AddSingleton<IWeatherProvider>(weather));

        var result = await RunForecastAsync(services);

        Assert.Equal(0, result.ActiveIncidents);
        Assert.Equal(0, weather.Requests);
    }

    private Task<ServiceProvider> CreateAsync() =>
        postgis.CreateServicesAsync(Now, s => s.AddSingleton<IWeatherProvider>(new FixedWeather()));

    private static async Task IngestSampleAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IngestionService>().RunOnceAsync(CancellationToken.None);
    }

    private static async Task<ForecastCycleResult> RunForecastAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ForecastService>().RunOnceAsync(CancellationToken.None);
    }

    /// <summary>A hot, dry meltemi afternoon everywhere: 34 °C, 18 %, 30 km/h from the north.</summary>
    private sealed class FixedWeather : IWeatherProvider
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<IReadOnlyList<HourlyWeather>>> GetHourlyAsync(IReadOnlyList<GeoPoint> points, int hours, CancellationToken ct)
        {
            Requests++;
            var start = new DateTimeOffset(Now.Year, Now.Month, Now.Day, Now.Hour, 0, 0, TimeSpan.Zero);
            IReadOnlyList<HourlyWeather> series = Enumerable.Range(0, hours)
                .Select(h => new HourlyWeather(start.AddHours(h), 34, 18, 30, 0, 48))
                .ToList();

            return Task.FromResult<IReadOnlyList<IReadOnlyList<HourlyWeather>>>(points.Select(_ => series).ToList());
        }
    }
}
