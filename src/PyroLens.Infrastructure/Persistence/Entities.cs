using NetTopologySuite.Geometries;
using PyroLens.Core.Detections;
using PyroLens.Core.Forecasting;
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

public sealed class IncidentForecast
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset WeatherFrom { get; set; }
    public short HorizonHours { get; set; }
    public double MeanTemperatureC { get; set; }
    public double MeanRelativeHumidityPercent { get; set; }
    public double MeanWindSpeedKmh { get; set; }
    public double MeanWindFromDegrees { get; set; }
    public double? MaxWindGustKmh { get; set; }
    public double DroughtFactor { get; set; }
    public double FuelLoadTonnesPerHectare { get; set; }
    public double PeakFfdi { get; set; }
    public FireDangerRating DangerRating { get; set; }
    public double PeakRateOfSpreadMetersPerHour { get; set; }
    public double HeadDistanceMeters { get; set; }
    public double LengthToBreadth { get; set; }
    public double SpreadBearingDegrees { get; set; }
    public Polygon SpreadArea { get; set; } = null!;
    public required string Model { get; set; }
}
