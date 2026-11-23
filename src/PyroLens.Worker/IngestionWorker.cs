using Microsoft.Extensions.Options;
using PyroLens.Infrastructure;
using PyroLens.Infrastructure.Ingestion;

namespace PyroLens.Worker;

/// <summary>Fetches FIRMS and clusters incidents every Ingestion:PollInterval.</summary>
public sealed class IngestionWorker(IServiceProvider services, IOptions<IngestionOptions> options, ILogger<IngestionWorker> logger)
    : PeriodicWorker(services, logger)
{
    protected override TimeSpan Interval => options.Value.PollInterval;

    protected override Task RunCycleAsync(IServiceProvider scopedServices, CancellationToken ct) =>
        scopedServices.GetRequiredService<IngestionService>().RunOnceAsync(ct);
}
