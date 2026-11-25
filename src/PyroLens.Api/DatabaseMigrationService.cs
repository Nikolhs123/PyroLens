using PyroLens.Infrastructure;

namespace PyroLens.Api;

/// <summary>Completes once the database schema is up to date, for background work that needs the tables.</summary>
public sealed class DatabaseReadiness
{
    private readonly TaskCompletionSource _migrated = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Migrated => _migrated.Task;

    internal void MarkMigrated() => _migrated.TrySetResult();
}

/// <summary>
/// Applies migrations in the background, retrying until the database answers. The API starts regardless,
/// so the map, /health/live and the logs stay available while the database is down or misconfigured;
/// /health/ready reports whether it can be reached.
/// </summary>
public sealed class DatabaseMigrationService(IServiceProvider services, DatabaseReadiness readiness, ILogger<DatabaseMigrationService> logger)
    : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await services.MigrateDatabaseAsync(stoppingToken);
                logger.LogInformation("Database is up to date");
                readiness.MarkMigrated();
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Database migration attempt {Attempt} failed; retrying in {Delay}", attempt, RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
