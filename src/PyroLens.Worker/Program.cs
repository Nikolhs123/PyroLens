using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Forecasting;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPyroLensIngestion(builder.Configuration);
builder.Services.AddPyroLensForecasting(builder.Configuration);

// `--once ingestion` or `--once forecast`: run one cycle and exit (scheduled jobs in Azure Container Apps).
// Without it: a long-running service with both loops (docker compose, local development).
var once = builder.Configuration["once"];
if (once is null)
{
    builder.Services.AddHostedService<IngestionWorker>();
    builder.Services.AddHostedService<ForecastWorker>();
    builder.Build().Run();
    return 0;
}

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PyroLens.Worker.Once");

try
{
    await host.Services.MigrateDatabaseAsync();
    await using var scope = host.Services.CreateAsyncScope();

    switch (once)
    {
        case "ingestion":
            await scope.ServiceProvider.GetRequiredService<IngestionService>().RunOnceAsync(CancellationToken.None);
            break;
        case "forecast":
            var result = await scope.ServiceProvider.GetRequiredService<ForecastService>().RunOnceAsync(CancellationToken.None);
            logger.LogInformation("Forecast: {Active} active incidents, {Written} forecasts written, {Skipped} skipped",
                result.ActiveIncidents, result.ForecastsWritten, result.Skipped);
            break;
        default:
            logger.LogError("Unknown --once value '{Once}'. Use 'ingestion' or 'forecast'.", once);
            return 2;
    }

    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "{Cycle} cycle failed", once);
    return 1;
}
