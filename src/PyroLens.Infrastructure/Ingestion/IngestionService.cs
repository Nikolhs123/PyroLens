using Microsoft.Extensions.Logging;
using PyroLens.Infrastructure.Firms;

namespace PyroLens.Infrastructure.Ingestion;

public sealed record IngestionCycleResult(
    int Products, int Received, int Inserted, int FlaggedIndustrial, int InsideArea, int FailedProducts, ClusteringResult Clustering);

/// <summary>One ingestion cycle: fetch every FIRMS product, store new detections, then group them into incidents.</summary>
public sealed class IngestionService(
    IFirmsSource source,
    DetectionWriter writer,
    IncidentClusterer clusterer,
    ILogger<IngestionService> logger)
{
    public async Task<IngestionCycleResult> RunOnceAsync(CancellationToken ct)
    {
        int products = 0, received = 0, inserted = 0, flagged = 0, insideArea = 0, failed = 0;

        foreach (var product in source.Products)
        {
            products++;

            // A failing product (timeout, bad key, outage) must not block the others.
            try
            {
                var parsed = await source.FetchAsync(product, ct);
                var result = await writer.WriteAsync(product, parsed.Detections, ct);

                received += result.Received;
                inserted += result.Inserted;
                flagged += result.FlaggedIndustrial;
                insideArea += result.InsideArea;

                logger.LogInformation(
                    "FIRMS {Product}: {Received} rows, {Inserted} new ({InsideArea} inside area of interest), {Flagged} likely industrial, {Skipped} unparseable",
                    product, result.Received, result.Inserted, result.InsideArea, result.FlaggedIndustrial, parsed.SkippedRows);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                logger.LogError(ex, "FIRMS {Product}: ingestion failed", product);
            }
        }

        var clustering = await clusterer.RunAsync(ct);
        logger.LogInformation(
            "Clustering: {Candidates} candidate detections, {Attached} joined open incidents, {NewIncidents} new incidents",
            clustering.Candidates, clustering.AttachedToExisting, clustering.NewIncidents);

        return new IngestionCycleResult(products, received, inserted, flagged, insideArea, failed, clustering);
    }
}
