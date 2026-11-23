using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PyroLens.Core.Detections;

namespace PyroLens.Infrastructure.Firms;

public interface IFirmsSource
{
    IReadOnlyList<string> Products { get; }

    Task<FirmsParseResult> FetchAsync(string product, CancellationToken ct);
}

/// <summary>Reads the FIRMS area API: /api/area/csv/{MAP_KEY}/{product}/{west,south,east,north}/{days}.</summary>
public sealed class FirmsApiSource(HttpClient http, IOptions<FirmsOptions> options) : IFirmsSource
{
    private readonly FirmsOptions _options = options.Value;

    public IReadOnlyList<string> Products => _options.Sources;

    public async Task<FirmsParseResult> FetchAsync(string product, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.MapKey))
        {
            throw new InvalidOperationException(
                "Firms:MapKey is not set. Set it with user-secrets or the Firms__MapKey environment variable, or use Firms:Mode=File.");
        }

        var path = $"api/area/csv/{Uri.EscapeDataString(_options.MapKey)}/{Uri.EscapeDataString(product)}/{_options.Area}/{_options.DayRange}";
        var csv = await http.GetStringAsync(path, ct);

        return FirmsCsvParser.Parse(csv, product.StartsWith("MODIS", StringComparison.OrdinalIgnoreCase) ? Instrument.Modis : Instrument.Viirs);
    }
}

/// <summary>
/// Replays a local CSV for development and demos without a MAP_KEY. Dates are moved forward by whole days
/// so that the newest row falls on today (UTC); repeated polls on the same day therefore yield identical rows.
/// </summary>
public sealed class FirmsFileSource(IOptions<FirmsOptions> options, TimeProvider clock, ILogger<FirmsFileSource> logger) : IFirmsSource
{
    public const string ProductName = "SAMPLE_SYNTHETIC";

    public IReadOnlyList<string> Products { get; } = [ProductName];

    public async Task<FirmsParseResult> FetchAsync(string product, CancellationToken ct)
    {
        var path = options.Value.SampleFile;
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        var parsed = FirmsCsvParser.Parse(await File.ReadAllTextAsync(path, ct));
        if (parsed.Detections.Count == 0)
        {
            return parsed;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var newest = DateOnly.FromDateTime(parsed.Detections.Max(d => d.AcquiredAt).UtcDateTime);
        var shiftDays = today.DayNumber - newest.DayNumber;

        logger.LogDebug("Replaying {Count} sample detections shifted by {Days} days", parsed.Detections.Count, shiftDays);

        return parsed with
        {
            Detections = parsed.Detections.Select(d => d with { AcquiredAt = d.AcquiredAt.AddDays(shiftDays) }).ToList(),
        };
    }
}
