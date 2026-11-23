using NetTopologySuite.Geometries;
using PyroLens.Core.Detections;
using PyroLens.Core.Incidents;

namespace PyroLens.Infrastructure.Persistence;

public sealed class FireDetection
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public Instrument Instrument { get; set; }
    public required string Satellite { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public Point Location { get; set; } = null!;
    public DateTimeOffset AcquiredAt { get; set; }
    public required string ConfidenceRaw { get; set; }
    public ConfidenceClass? ConfidenceClass { get; set; }
    public short? ConfidencePercent { get; set; }
    public double? BrightnessKelvin { get; set; }
    public double? FrpMegawatts { get; set; }
    public bool? IsDaytime { get; set; }
    public bool LikelyIndustrial { get; set; }

    /// <summary>Area of interest the detection falls in (with its buffer), e.g. "GRC"; null when outside all of them.</summary>
    public string? AreaCode { get; set; }

    public long? IncidentId { get; set; }
    public Incident? Incident { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}

public sealed class Incident
{
    public long Id { get; set; }
    public IncidentStatus Status { get; set; }
    public Point Centroid { get; set; } = null!;
    public Geometry? Footprint { get; set; }
    public DateTimeOffset FirstDetectedAt { get; set; }
    public DateTimeOffset LastDetectedAt { get; set; }
    public int DetectionCount { get; set; }
    public double? MaxFrpMegawatts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<FireDetection> Detections { get; } = [];
}
