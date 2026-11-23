using Microsoft.Extensions.Options;
using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Forecasting;

namespace PyroLens.Worker;

/// <summary>Computes weather-driven spread forecasts for active incidents every Forecast:Interval.</summary>
public sealed class ForecastWorker(IServiceProvider services, IOptions<ForecastOptions> options, ILogger<ForecastWorker> logger)
    : PeriodicWorker(services, logger)
{
    protected override TimeSpan Interval => options.Value.Interval;

    protected override TimeSpan InitialDelay => options.Value.InitialDelay;

    protected override async Task RunCycleAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var result = await scopedServices.GetRequiredService<ForecastService>().RunOnceAsync(ct);
        logger.LogInformation(
            "Forecast: {Active} active incidents, {Written} forecasts written, {Skipped} skipped",
            result.ActiveIncidents, result.ForecastsWritten, result.Skipped);
    }
}
