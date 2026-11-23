using Microsoft.Extensions.Options;
using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Ingestion;

namespace PyroLens.Worker;

/// <summary>Runs an ingestion cycle at start-up and then every Ingestion:PollInterval.</summary>
public sealed class IngestionWorker(
    IServiceProvider services,
    IOptions<IngestionOptions> options,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var migrated = false;
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        do
        {
            try
            {
                if (!migrated)
                {
                    await services.MigrateDatabaseAsync(stoppingToken);
                    migrated = true;
                }

                await using var scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IngestionService>().RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Keep polling: the next cycle may succeed (database restart, network blip).
                logger.LogError(ex, "Ingestion cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
