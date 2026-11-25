using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PyroLens.Core.Forecasting;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Jobs;
using PyroLens.Infrastructure.Persistence;
using PyroLens.Infrastructure.Weather;

namespace PyroLens.IntegrationTests;

/// <summary>job_runs: who ran what when, and nobody running the same job twice at once.</summary>
[Collection(PostgisCollection.Name)]
public sealed class JobRunnerTests(PostgisFixture postgis)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task A_run_is_recorded_with_who_started_it()
    {
        await using var services = await postgis.CreateServicesAsync(Now);

        Assert.Equal(JobOutcome.Succeeded, await RunAsync(services, BackgroundJob.Ingestion, "website"));

        var run = await RowAsync(services, "ingestion");
        Assert.Equal("website", run.LastStartedBy);
        Assert.Equal(Now, run.LastStartedAt);
        Assert.Equal(Now, run.LastSucceededAt);
        Assert.False(run.IsRunning(Now, JobRunner.StuckAfter));
    }

    [Fact]
    public async Task A_job_is_skipped_until_the_gap_has_passed()
    {
        var clock = new MutableClock(Now);
        await using var services = await postgis.CreateServicesAsync(Now, s => s.AddSingleton<TimeProvider>(clock));

        Assert.Equal(JobOutcome.Succeeded, await RunAsync(services, BackgroundJob.Ingestion, "schedule"));
        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(JobOutcome.Skipped, await RunAsync(services, BackgroundJob.Ingestion, "website"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(JobOutcome.Succeeded, await RunAsync(services, BackgroundJob.Ingestion, "website"));

        var run = await RowAsync(services, "ingestion");
        Assert.Equal("website", run.LastStartedBy);
        Assert.Equal(Now.AddMinutes(10), run.LastSucceededAt);
    }

    [Fact]
    public async Task A_running_job_is_not_started_again_until_it_looks_stuck()
    {
        var clock = new MutableClock(Now);
        await using var services = await postgis.CreateServicesAsync(Now, s => s.AddSingleton<TimeProvider>(clock));
        await using (var scope = services.CreateAsyncScope())
        {
            // Another process started 5 minutes ago and has not finished.
            var startedAt = Now.AddMinutes(-5);
            await scope.ServiceProvider.GetRequiredService<PyroLensDbContext>().Database.ExecuteSqlAsync(
                $"INSERT INTO job_runs (job, last_started_at, last_started_by) VALUES ('ingestion', {startedAt}, 'schedule')");
        }

        // Even with no gap at all, a run still going is left alone.
        Assert.Equal(JobOutcome.Skipped, await RunAsync(services, BackgroundJob.Ingestion, "worker", TimeSpan.Zero));
        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(JobOutcome.Skipped, await RunAsync(services, BackgroundJob.Ingestion, "worker", TimeSpan.Zero));

        // 15 minutes without finishing: its process is assumed dead and the run is taken over.
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(JobOutcome.Succeeded, await RunAsync(services, BackgroundJob.Ingestion, "worker", TimeSpan.Zero));
        Assert.Equal("worker", (await RowAsync(services, "ingestion")).LastStartedBy);
    }

    [Fact]
    public async Task A_failed_run_keeps_the_last_success()
    {
        var clock = new MutableClock(Now);
        var weather = new FlakyWeather();
        await using var services = await postgis.CreateServicesAsync(Now, s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            s.AddSingleton<IWeatherProvider>(weather);
        });
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IngestionService>().RunOnceAsync(default);
        }

        Assert.Equal(JobOutcome.Succeeded, await RunAsync(services, BackgroundJob.Forecast, "worker"));

        clock.Advance(TimeSpan.FromMinutes(30));
        weather.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync(services, BackgroundJob.Forecast, "worker"));

        var run = await RowAsync(services, "forecast");
        Assert.Equal(Now, run.LastSucceededAt);
        Assert.Equal(Now.AddMinutes(30), run.LastFinishedAt);
        Assert.False(run.IsRunning(clock.GetUtcNow(), JobRunner.StuckAfter));
    }

    [Fact]
    public async Task Ten_processes_asking_at_once_run_the_job_once()
    {
        await using var services = await postgis.CreateServicesAsync(Now);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => RunAsync(services, BackgroundJob.Ingestion, $"p{i}")));

        Assert.Equal(1, outcomes.Count(o => o == JobOutcome.Succeeded));
        Assert.Equal(9, outcomes.Count(o => o == JobOutcome.Skipped));
    }

    // Each call has its own scope, DbContext and connection, like separate processes.
    private static async Task<JobOutcome> RunAsync(IServiceProvider services, BackgroundJob job, string by, TimeSpan? gap = null)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<JobRunner>().RunAsync(job, by, gap ?? Gap, default);
    }

    private static async Task<JobRun> RowAsync(IServiceProvider services, string job)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PyroLensDbContext>().JobRuns.AsNoTracking().SingleAsync(r => r.Job == job);
    }

    private sealed class FlakyWeather : IWeatherProvider
    {
        public bool Fail { get; set; }

        public Task<IReadOnlyList<IReadOnlyList<HourlyWeather>>> GetHourlyAsync(IReadOnlyList<GeoPoint> points, int hours, CancellationToken ct)
        {
            if (Fail)
            {
                throw new HttpRequestException("Open-Meteo is down");
            }

            var start = new DateTimeOffset(Now.Year, Now.Month, Now.Day, Now.Hour, 0, 0, TimeSpan.Zero);
            IReadOnlyList<HourlyWeather> series = Enumerable.Range(0, hours)
                .Select(h => new HourlyWeather(start.AddHours(h), 25, 40, 10, 90, 15))
                .ToList();
            return Task.FromResult<IReadOnlyList<IReadOnlyList<HourlyWeather>>>(points.Select(_ => series).ToList());
        }
    }
}
