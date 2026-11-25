using Microsoft.Extensions.Options;
using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Jobs;

namespace PyroLens.Worker;

/// <summary>Computes weather-driven spread forecasts for active incidents every Forecast:Interval.</summary>
public sealed class ForecastWorker(IServiceProvider services, IOptions<ForecastOptions> options, ILogger<ForecastWorker> logger)
    : PeriodicWorker(services, logger)
{
    protected override TimeSpan Interval => options.Value.Interval;

    protected override TimeSpan InitialDelay => options.Value.InitialDelay;

    protected override Task RunCycleAsync(IServiceProvider scopedServices, CancellationToken ct) =>
        scopedServices.GetRequiredService<JobRunner>().RunAsync(BackgroundJob.Forecast, "worker", TimeSpan.Zero, ct);
}
