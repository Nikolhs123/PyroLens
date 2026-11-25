using Microsoft.Extensions.Options;
using PyroLens.Infrastructure.Jobs;

namespace PyroLens.Api;

public sealed class LiveRefreshOptions
{
    public const string Section = "LiveRefresh";

    /// <summary>Off by default: locally the worker container does this job.</summary>
    public bool Enabled { get; set; }

    public TimeSpan IngestionEvery { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan ForecastEvery { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How often to look at <c>job_runs</c> to see whether a job is due.</summary>
    public TimeSpan CheckEvery { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// While the website is awake, keeps its data fresh by running the worker's jobs itself when they are due.
/// On Render's free plan the site sleeps after 15 minutes without visitors, so in practice this fetches new fires
/// when someone opens the site; the GitHub schedule covers the hours in between as best it can. Both record their runs
/// in <c>job_runs</c> through <see cref="JobRunner"/>, so they never repeat each other's work.
/// </summary>
public sealed class LiveRefreshService(
    IServiceScopeFactory scopes,
    DatabaseReadiness readiness,
    IOptions<LiveRefreshOptions> options,
    ILogger<LiveRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        await readiness.Migrated.WaitAsync(stoppingToken);
        logger.LogInformation("Live refresh on: fires every {Ingestion}, weather every {Forecast}", o.IngestionEvery, o.ForecastEvery);

        using var timer = new PeriodicTimer(o.CheckEvery);
        do
        {
            // Fires first, so new incidents get a forecast in the same round.
            await RunIfDueAsync(BackgroundJob.Ingestion, o.IngestionEvery, stoppingToken);
            await RunIfDueAsync(BackgroundJob.Forecast, o.ForecastEvery, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunIfDueAsync(BackgroundJob job, TimeSpan every, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<JobRunner>().RunAsync(job, "website", every, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // An unhandled exception here would stop the whole web server; the next round tries again.
            logger.LogError(ex, "Live refresh: {Job} failed", job);
        }
    }
}
