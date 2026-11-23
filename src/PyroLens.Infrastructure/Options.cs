using System.ComponentModel.DataAnnotations;

namespace PyroLens.Infrastructure;

public enum FirmsMode
{
    /// <summary>Replay the synthetic sample file. Needs no MAP_KEY.</summary>
    File,

    /// <summary>Call the NASA FIRMS area API. Needs a MAP_KEY.</summary>
    Api,
}

public sealed class FirmsOptions
{
    public const string Section = "Firms";

    public FirmsMode Mode { get; set; } = FirmsMode.File;

    /// <summary>Free key from https://firms.modaps.eosdis.nasa.gov/api/map_key/. Keep it out of source control.</summary>
    public string? MapKey { get; set; }

    public string BaseUrl { get; set; } = "https://firms.modaps.eosdis.nasa.gov/";

    public static readonly IReadOnlyList<string> DefaultSources = ["VIIRS_SNPP_NRT", "VIIRS_NOAA20_NRT", "VIIRS_NOAA21_NRT", "MODIS_NRT"];

    /// <summary>FIRMS products to poll. Empty means <see cref="DefaultSources"/>.</summary>
    /// <remarks>No initializer on purpose: the configuration binder appends to an existing list instead of replacing it.</remarks>
    public List<string> Sources { get; set; } = [];

    /// <summary>Area as west,south,east,north. The default covers Greece.</summary>
    public BoundingBox Area { get; set; } = new();

    /// <summary>Days back to request. Overlapping polls are harmless because ingestion is idempotent.</summary>
    [Range(1, 5)]
    public int DayRange { get; set; } = 2;

    public string SampleFile { get; set; } = "samples/firms_sample_synthetic.csv";
}

public sealed class BoundingBox
{
    public double West { get; set; } = 19.3;
    public double South { get; set; } = 34.7;
    public double East { get; set; } = 29.7;
    public double North { get; set; } = 41.8;

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{West},{South},{East},{North}");
}

/// <summary>Which detections count as fire. Defaults follow foties.gr: MODIS above 70%, VIIRS nominal or high.</summary>
public sealed class DetectionFilterOptions
{
    public const string Section = "DetectionFilter";

    /// <summary>MODIS detections need a confidence strictly above this value.</summary>
    [Range(0, 100)]
    public int ModisConfidenceAbove { get; set; } = 70;

    public static readonly IReadOnlyList<string> DefaultViirsConfidence = ["nominal", "high"];

    /// <summary>Accepted VIIRS confidence classes. Empty means <see cref="DefaultViirsConfidence"/>.</summary>
    /// <remarks>No initializer on purpose: the configuration binder appends to an existing list instead of replacing it.</remarks>
    public List<string> ViirsConfidence { get; set; } = [];

    /// <summary>A detection is flagged as a likely industrial heat source (refinery, factory) when the same spot
    /// also burned on each of the previous <see cref="IndustrialConsecutiveDays"/> days.</summary>
    [Range(1, 30)]
    public int IndustrialConsecutiveDays { get; set; } = 3;

    [Range(50, 5000)]
    public double IndustrialRadiusMeters { get; set; } = 500;

    /// <summary>
    /// Only detections inside an area of interest (Greece plus a 2 km buffer) open incidents and appear in the API
    /// by default. The FIRMS bounding box is just the fetch window and also covers neighbouring countries.
    /// </summary>
    public bool RequireAreaOfInterest { get; set; } = true;
}

public sealed class ClusteringOptions
{
    public const string Section = "Clustering";

    /// <summary>Only detections acquired within this many hours start or join incidents.</summary>
    [Range(1, 168)]
    public int WindowHours { get; set; } = 24;

    /// <summary>DBSCAN distance between detections of the same new incident.</summary>
    [Range(100, 20000)]
    public double ClusterDistanceMeters { get; set; } = 1500;

    /// <summary>A new detection this close to an open incident joins it instead of starting a new one.</summary>
    [Range(100, 20000)]
    public double AttachDistanceMeters { get; set; } = 2000;

    /// <summary>Incidents with no detection for longer than this stop absorbing new detections.</summary>
    [Range(1, 720)]
    public int IncidentActiveHours { get; set; } = 48;
}

public sealed class ForecastOptions
{
    public const string Section = "Forecast";

    public string OpenMeteoBaseUrl { get; set; } = "https://api.open-meteo.com/";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Lets the first ingestion cycle create incidents before the first forecast runs.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Incidents with a detection in this many hours get a forecast.</summary>
    [Range(1, 168)]
    public int ActiveIncidentHours { get; set; } = 24;

    /// <summary>Locations per Open-Meteo request.</summary>
    [Range(1, 100)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// McArthur drought factor, 1 (wet) to 10 (fully cured fuel). A fixed assumption for now:
    /// 10 suits a Greek summer, lower values suit the wet season.
    /// </summary>
    [Range(1, 10)]
    public double DroughtFactor { get; set; } = 8;

    /// <summary>Fine fuel load in tonnes per hectare. 12 t/ha is a middle value for Mediterranean shrubland.</summary>
    [Range(1, 60)]
    public double FuelLoadTonnesPerHectare { get; set; } = 12;

    public static readonly IReadOnlyList<int> Horizons = [1, 3];
}

public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(10);
}
