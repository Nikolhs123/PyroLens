using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PyroLens.Core.Detections;
using PyroLens.Core.Forecasting;
using PyroLens.Core.Incidents;
using PyroLens.Infrastructure.Forecasting;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Operations;
using PyroLens.Infrastructure.Persistence;
using PyroLens.Infrastructure.Weather;

namespace PyroLens.IntegrationTests;

/// <summary>
/// The escalation rules on real data: the facts query reads pixels, forecasts, units and the timeline.
/// The sample's three incidents (6, 3 and 2 pixels) were all seen between 00:10 and 01:27 UTC; the clock starts at 06:00.
/// </summary>
[Collection(PostgisCollection.Name)]
public sealed class AttentionTests(PostgisFixture postgis)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 6, 0, 0, TimeSpan.Zero);
    private const string Actor = "test dispatcher";

    [Fact]
    public async Task Sample_fires_with_three_or_more_recent_pixels_are_spreading()
    {
        await using var env = await CreateAsync();

        var flags = await env.EvaluateAsync();

        Assert.Equal(new Attention(AttentionRule.Spreading, AttentionLevel.Watch, Pixels: 6, WithinHours: 6), Assert.Single(flags[env.Parnitha]));
        Assert.Equal(3, Assert.Single(flags[env.Evia]).Pixels);
        Assert.Empty(flags[env.Smallest]);

        // Seven hours later the same pixels are no longer recent.
        env.Clock.Advance(TimeSpan.FromHours(7));
        Assert.All((await env.EvaluateAsync()).Values, flags => Assert.Empty(flags));
    }

    [Fact]
    public async Task No_units_follows_confirm_send_and_release()
    {
        await using var env = await CreateAsync();
        var fire = env.Smallest; // no other flags to get in the way
        var engine = await env.Db.Resources.Where(r => r.Callsign == "ENG-01").Select(r => r.Id).SingleAsync();

        await env.Ok(env.Incidents.ChangeStatusAsync(fire, IncidentStatus.Detected, IncidentStatus.Confirmed, Actor, null, default));
        var confirmedAt = env.Clock.GetUtcNow();
        env.Clock.Advance(TimeSpan.FromMinutes(19));
        Assert.Empty((await env.EvaluateAsync())[fire]);

        env.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new Attention(AttentionRule.NoUnits, AttentionLevel.Urgent, Since: confirmedAt), Assert.Single((await env.EvaluateAsync())[fire]));

        // A unit on its way clears it.
        await env.Ok(env.Resources.ReserveAsync(engine, fire, Actor, default));
        await env.Ok(env.Resources.AssignAsync(engine, fire, Actor, default));
        Assert.Empty((await env.EvaluateAsync())[fire]);

        // Released: the clock starts again from the release.
        env.Clock.Advance(TimeSpan.FromMinutes(30));
        await env.Ok(env.Resources.ReleaseAsync(engine, fire, Actor, default));
        var releasedAt = env.Clock.GetUtcNow();
        env.Clock.Advance(TimeSpan.FromMinutes(19));
        Assert.Empty((await env.EvaluateAsync())[fire]);
        env.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(releasedAt, Assert.Single((await env.EvaluateAsync())[fire]).Since);
    }

    [Fact]
    public async Task A_contained_fire_with_a_new_pixel_is_burning_again()
    {
        await using var env = await CreateAsync();
        var fire = env.Parnitha;
        await env.Ok(env.Incidents.ChangeStatusAsync(fire, IncidentStatus.Detected, IncidentStatus.Confirmed, Actor, null, default));
        await env.Ok(env.Incidents.ChangeStatusAsync(fire, IncidentStatus.Confirmed, IncidentStatus.Active, Actor, null, default));
        await env.Ok(env.Incidents.ChangeStatusAsync(fire, IncidentStatus.Active, IncidentStatus.Contained, Actor, null, default));
        var containedAt = env.Clock.GetUtcNow();

        // Contained, and the old pixels from before containment do not count.
        Assert.Empty((await env.EvaluateAsync())[fire]);

        // A new pass sees heat 300 m from the old pixels; ingestion attaches it to the contained incident.
        env.Clock.Advance(TimeSpan.FromHours(1));
        var centre = await env.Db.Incidents.Where(i => i.Id == fire).Select(i => i.Centroid).SingleAsync();
        await env.Writer.WriteAsync("TEST", [Viirs(centre.Y + 0.0027, centre.X, env.Clock.GetUtcNow().AddMinutes(-10))], default);
        await env.Clusterer.RunAsync(default);

        var flag = Assert.Single((await env.EvaluateAsync())[fire]);
        Assert.Equal(new Attention(AttentionRule.BurningAgain, AttentionLevel.Urgent, Since: containedAt, Pixels: 1), flag);
    }

    [Fact]
    public async Task Rising_danger_is_read_from_the_last_two_forecasts()
    {
        await using var env = await CreateAsync();
        var fire = env.Smallest;

        // A mild morning: low-moderate everywhere.
        env.Weather.Set(temperatureC: 20, humidityPercent: 60, windKmh: 5);
        await env.ForecastAsync();
        Assert.Empty((await env.EvaluateAsync())[fire]);

        // An hour later the meltemi arrives: hot, dry, 30 km/h.
        env.Clock.Advance(TimeSpan.FromHours(1));
        env.Weather.Set(temperatureC: 34, humidityPercent: 18, windKmh: 30);
        await env.ForecastAsync();

        var flag = Assert.Single((await env.EvaluateAsync())[fire]);
        Assert.Equal(
            new Attention(AttentionRule.Danger, AttentionLevel.Urgent, From: FireDangerRating.LowModerate, To: FireDangerRating.VeryHigh),
            flag);
    }

    // ---------- Setup ----------

    private async Task<Env> CreateAsync()
    {
        var clock = new MutableClock(Now);
        var weather = new SettableWeather(clock);
        var services = await postgis.CreateServicesAsync(Now, s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            s.AddSingleton<IWeatherProvider>(weather);
        });
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IngestionService>().RunOnceAsync(default);
        }

        var root = services.CreateAsyncScope();
        var bySize = await root.ServiceProvider.GetRequiredService<PyroLensDbContext>().Incidents.AsNoTracking()
            .OrderByDescending(i => i.DetectionCount).Select(i => i.Id).ToListAsync();
        return new Env(services, root, clock, weather, bySize);
    }

    private static RawDetection Viirs(double lat, double lon, DateTimeOffset at) =>
        new(lat, lon, at, Instrument.Viirs, "N", "h", ConfidenceClass.High, null, 330, 10, false);

    private sealed class Env(ServiceProvider services, AsyncServiceScope scope, MutableClock clock, SettableWeather weather, List<long> bySize)
        : IAsyncDisposable
    {
        public MutableClock Clock { get; } = clock;
        public SettableWeather Weather { get; } = weather;
        public long Parnitha { get; } = bySize[0];
        public long Evia { get; } = bySize[1];
        public long Smallest { get; } = bySize[2];
        public PyroLensDbContext Db => scope.ServiceProvider.GetRequiredService<PyroLensDbContext>();
        public IncidentOperations Incidents => scope.ServiceProvider.GetRequiredService<IncidentOperations>();
        public ResourceOperations Resources => scope.ServiceProvider.GetRequiredService<ResourceOperations>();
        public DetectionWriter Writer => scope.ServiceProvider.GetRequiredService<DetectionWriter>();
        public IncidentClusterer Clusterer => scope.ServiceProvider.GetRequiredService<IncidentClusterer>();

        public async Task<Dictionary<long, IReadOnlyList<Attention>>> EvaluateAsync()
        {
            await using var s = services.CreateAsyncScope();
            return await s.ServiceProvider.GetRequiredService<AttentionService>().EvaluateAsync([Parnitha, Evia, Smallest], default);
        }

        public async Task ForecastAsync()
        {
            await using var s = services.CreateAsyncScope();
            await s.ServiceProvider.GetRequiredService<ForecastService>().RunOnceAsync(default);
        }

        public async Task Ok<T>(Task<OperationResult<T>> action) => Assert.Equal(Outcome.Ok, (await action).Outcome);

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await services.DisposeAsync();
        }
    }

    /// <summary>The same weather everywhere, starting at the current hour of the test clock.</summary>
    private sealed class SettableWeather(TimeProvider clock) : IWeatherProvider
    {
        private (double T, double Rh, double Wind) _now = (25, 40, 10);

        public void Set(double temperatureC, double humidityPercent, double windKmh) => _now = (temperatureC, humidityPercent, windKmh);

        public Task<IReadOnlyList<IReadOnlyList<HourlyWeather>>> GetHourlyAsync(IReadOnlyList<GeoPoint> points, int hours, CancellationToken ct)
        {
            var t = clock.GetUtcNow();
            var start = new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, TimeSpan.Zero);
            IReadOnlyList<HourlyWeather> series = Enumerable.Range(0, hours)
                .Select(h => new HourlyWeather(start.AddHours(h), _now.T, _now.Rh, _now.Wind, 0, _now.Wind * 1.5))
                .ToList();
            return Task.FromResult<IReadOnlyList<IReadOnlyList<HourlyWeather>>>(points.Select(_ => series).ToList());
        }
    }
}
