using PyroLens.Infrastructure;

namespace PyroLens.Worker;

/// <summary>
/// Runs a cycle at start-up (after <see cref="InitialDelay"/>) and then every <see cref="Interval"/>.
/// Applies migrations first, and keeps going when a cycle fails: the next one may succeed.
/// </summary>
public abstract class PeriodicWorker(IServiceProvider services, ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }

    protected virtual TimeSpan InitialDelay => TimeSpan.Zero;

    protected abstract Task RunCycleAsync(IServiceProvider scopedServices, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (InitialDelay > TimeSpan.Zero)
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }

        var migrated = false;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                if (!migrated)
                {
                    // Safe to run from several workers at once: the migrator holds an advisory lock.
                    await services.MigrateDatabaseAsync(stoppingToken);
                    migrated = true;
                }

                await using var scope = services.CreateAsyncScope();
                await RunCycleAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "{Worker} cycle failed", GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
