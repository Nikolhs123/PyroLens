using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Jobs;
using PyroLens.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPyroLensIngestion(builder.Configuration);
builder.Services.AddPyroLensForecasting(builder.Configuration);

// `--once ingestion` or `--once forecast`: run one cycle and exit (the GitHub schedule, or Azure Container Apps jobs).
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

BackgroundJob job;
switch (once)
{
    case "ingestion":
        job = BackgroundJob.Ingestion;
        break;
    case "forecast":
        job = BackgroundJob.Forecast;
        break;
    default:
        logger.LogError("Unknown --once value '{Once}'. Use 'ingestion' or 'forecast'.", once);
        return 2;
}

try
{
    await host.Services.MigrateDatabaseAsync();
    await using var scope = host.Services.CreateAsyncScope();

    // If the website (or another scheduled run) did this job in the last few minutes, there is nothing new to fetch.
    var outcome = await scope.ServiceProvider.GetRequiredService<JobRunner>()
        .RunAsync(job, "schedule", TimeSpan.FromMinutes(5), CancellationToken.None);
    logger.LogInformation("{Job} cycle: {Outcome}", job, outcome);

    return outcome == JobOutcome.Failed ? 1 : 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "{Cycle} cycle failed", once);
    return 1;
}
