using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PyroLens.Infrastructure.Forecasting;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Jobs;

public enum BackgroundJob
{
    /// <summary>Fetch FIRMS fire pixels and group them into incidents.</summary>
    Ingestion,

    /// <summary>Fetch weather and compute spread forecasts.</summary>
    Forecast,
}

public enum JobOutcome
{
    Succeeded,
    Failed,

    /// <summary>Someone else ran it recently, or is running it now.</summary>
    Skipped,
}

/// <summary>
/// Runs an ingestion or forecast cycle for whoever asks (the worker's loops, the GitHub schedule, the website while
/// someone is looking at it) and records it in <c>job_runs</c>. Two processes therefore never run the same job at the
/// same time or right after each other, and the site can say how fresh its data is.
/// </summary>
public sealed class JobRunner(PyroLensDbContext db, IServiceProvider services, TimeProvider clock, ILogger<JobRunner> logger)
{
    /// <summary>A run that has not finished after this long is assumed dead (its process was killed) and may be replaced.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    /// <summary>Database spelling of a job: "ingestion" or "forecast".</summary>
    public static string Name(BackgroundJob job) => PyroLensDbContext.ToSnakeCase(job);

    /// <summary>
    /// Runs the job unless it started less than <paramref name="skipIfStartedWithin"/> ago or is still running.
    /// Exceptions from the cycle are recorded as a failed run and then rethrown.
    /// </summary>
    /// <param name="startedBy">Who asks, shown on the site: "worker", "schedule" or "website".</param>
    public async Task<JobOutcome> RunAsync(BackgroundJob job, string startedBy, TimeSpan skipIfStartedWithin, CancellationToken ct)
    {
        var started = ToMicroseconds(clock.GetUtcNow());
        if (!await TryClaimAsync(job, startedBy, started, skipIfStartedWithin, ct))
        {
            logger.LogInformation("{Job}: skipped, it started less than {Gap} ago or is still running", job, skipIfStartedWithin);
            return JobOutcome.Skipped;
        }

        var succeeded = false;
        try
        {
            succeeded = await RunCycleAsync(job, ct);
            return succeeded ? JobOutcome.Succeeded : JobOutcome.Failed;
        }
        finally
        {
            await FinishAsync(job, started, succeeded);
        }
    }

    /// <summary>
    /// One statement: insert the job's row, or take it over if the last run started long enough ago and is not still
    /// going. Two processes asking at the same moment cannot both win, because PostgreSQL locks the row for the update.
    /// </summary>
    private async Task<bool> TryClaimAsync(BackgroundJob job, string startedBy, DateTimeOffset now, TimeSpan gap, CancellationToken ct)
    {
        var name = Name(job);
        var dueBefore = now - gap;
        var stuckBefore = now - StuckAfter;

        var claimed = await db.Database.ExecuteSqlAsync($$"""
            INSERT INTO job_runs AS j (job, last_started_at, last_started_by)
            VALUES ({{name}}, {{now}}, {{startedBy}})
            ON CONFLICT (job) DO UPDATE
                SET last_started_at = EXCLUDED.last_started_at,
                    last_started_by = EXCLUDED.last_started_by
                WHERE j.last_started_at <= {{dueBefore}}
                  AND (j.last_finished_at >= j.last_started_at OR j.last_started_at <= {{stuckBefore}})
            """, ct);

        return claimed == 1;
    }

    private async Task<bool> RunCycleAsync(BackgroundJob job, CancellationToken ct)
    {
        switch (job)
        {
            case BackgroundJob.Ingestion:
                var ingestion = await services.GetRequiredService<IngestionService>().RunOnceAsync(ct);
                // When every FIRMS product failed (bad key, NASA outage) nothing was checked.
                return ingestion.FailedProducts < ingestion.Products;

            case BackgroundJob.Forecast:
                var forecast = await services.GetRequiredService<ForecastService>().RunOnceAsync(ct);
                logger.LogInformation(
                    "Forecast: {Active} active incidents, {Written} forecasts written, {Skipped} skipped, {Deleted} old forecasts deleted",
                    forecast.ActiveIncidents, forecast.ForecastsWritten, forecast.Skipped, forecast.Deleted);
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(job), job, null);
        }
    }

    /// <summary>Only the run that claimed the row may finish it: a run replaced as stuck must not overwrite its successor.</summary>
    private async Task FinishAsync(BackgroundJob job, DateTimeOffset started, bool succeeded)
    {
        var name = Name(job);
        var now = ToMicroseconds(clock.GetUtcNow());

        try
        {
            await db.Database.ExecuteSqlAsync($"""
                UPDATE job_runs
                SET last_finished_at = {now},
                    last_succeeded_at = CASE WHEN {succeeded} THEN {now} ELSE last_succeeded_at END
                WHERE job = {name} AND last_started_at = {started}
                """, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never hide the cycle's own outcome or exception; the row counts as stuck and is taken over later.
            logger.LogError(ex, "{Job}: could not record the end of the run", job);
        }
    }

    /// <summary>PostgreSQL keeps microseconds; rounding here keeps the value equal after a round trip.</summary>
    private static DateTimeOffset ToMicroseconds(DateTimeOffset t) => new(t.UtcTicks - t.UtcTicks % 10, TimeSpan.Zero);
}
