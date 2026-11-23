using PyroLens.Core.Detections;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure.Ingestion;

public static class DetectionQueries
{
    /// <summary>
    /// Keeps detections that count as fire under the given confidence rule.
    /// Used both by clustering and by the API, so the two always agree.
    /// </summary>
    public static IQueryable<FireDetection> WherePassesConfidence(
        this IQueryable<FireDetection> query, int modisConfidenceAbove, IReadOnlyCollection<ConfidenceClass> viirsAccepted)
    {
        var accepted = viirsAccepted.Cast<ConfidenceClass?>().ToList();

        return query.Where(d =>
            (d.Instrument == Instrument.Modis && d.ConfidencePercent > modisConfidenceAbove) ||
            (d.Instrument == Instrument.Viirs && accepted.Contains(d.ConfidenceClass)));
    }

    /// <summary>Keeps detections inside an area of interest (with its buffer) when <paramref name="required"/> is true.</summary>
    public static IQueryable<FireDetection> WhereInsideAreaOfInterest(this IQueryable<FireDetection> query, bool required) =>
        required ? query.Where(d => d.AreaCode != null) : query;

    public static IReadOnlyList<ConfidenceClass> ParseViirsClasses(IEnumerable<string> values) =>
        values
            .Select(v => Enum.TryParse<ConfidenceClass>(v.Trim(), true, out var c) ? c : throw new ArgumentException($"Unknown VIIRS confidence '{v}'. Use low, nominal or high."))
            .Distinct()
            .ToList();
}
