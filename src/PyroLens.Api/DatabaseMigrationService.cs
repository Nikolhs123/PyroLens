using PyroLens.Infrastructure;

namespace PyroLens.Api;

/// <summary>
/// Applies migrations in the background, retrying until the database answers. The API starts regardless,
/// so the map, /health/live and the logs stay available while the database is down or misconfigured;
/// /health/ready reports whether it can be reached.
/// </summary>
public sealed class DatabaseMigrationService(IServiceProvider services, ILogger<DatabaseMigrationService> logger) : BackgroundService
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
