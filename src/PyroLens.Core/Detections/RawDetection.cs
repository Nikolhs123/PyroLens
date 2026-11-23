namespace PyroLens.Core.Detections;

public enum Instrument
{
    Modis,
    Viirs,
}

/// <summary>VIIRS reports confidence as a class; MODIS reports a percentage instead.</summary>
public enum ConfidenceClass
{
    Low,
    Nominal,
    High,
}

/// <summary>
/// One fire pixel as reported by a FIRMS product, before it is stored or grouped into an incident.
/// </summary>
public sealed record RawDetection(
    double Latitude,
    double Longitude,
    DateTimeOffset AcquiredAt,
    Instrument Instrument,
    string Satellite,
    string ConfidenceRaw,
    ConfidenceClass? ConfidenceClass,
    short? ConfidencePercent,
    double? BrightnessKelvin,
    double? FrpMegawatts,
    bool? IsDaytime);
