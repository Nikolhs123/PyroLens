namespace PyroLens.Core.Forecasting;

/// <summary>Legacy McArthur fire danger rating bands (FFDI 0–11, 12–24, 25–49, 50–74, 75–99, 100+).</summary>
public enum FireDangerRating
{
    LowModerate,
    High,
    VeryHigh,
    Severe,
    Extreme,
    Catastrophic,
}

/// <summary>
/// McArthur Mk5 forest fire behaviour, as implemented in CSIRO's Spark model library:
/// https://research.csiro.au/spark/resources/model-library/mcarthur
/// A simplified, indicative model: no terrain, no vegetation map, no suppression.
/// </summary>
public static class FireBehaviour
{
    public const string ModelName = "McArthur Mk5 FFDI and rate of spread (Noble et al. 1980), elliptical spread (CSIRO Spark)";

    /// <summary>
    /// Forest Fire Danger Index: FFDI = 2·exp(−0.450 + 0.987·ln(DF) − 0.0345·RH + 0.0338·T + 0.0234·V).
    /// </summary>
    /// <param name="temperatureC">Air temperature, °C.</param>
    /// <param name="relativeHumidityPercent">Relative humidity, %.</param>
    /// <param name="windSpeedKmh">Wind speed at 10 m, km/h.</param>
    /// <param name="droughtFactor">Fuel dryness, 1 (wet) to 10 (fully cured).</param>
    public static double ForestFireDangerIndex(double temperatureC, double relativeHumidityPercent, double windSpeedKmh, double droughtFactor)
    {
        if (droughtFactor is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(droughtFactor), droughtFactor, "The drought factor ranges from 1 to 10.");
        }

        var rh = Math.Clamp(relativeHumidityPercent, 0, 100);
        var wind = Math.Max(windSpeedKmh, 0);

        return 2.0 * Math.Exp(-0.450 + 0.987 * Math.Log(droughtFactor) - 0.0345 * rh + 0.0338 * temperatureC + 0.0234 * wind);
    }

    public static FireDangerRating Rate(double ffdi) => ffdi switch
    {
        < 12 => FireDangerRating.LowModerate,
        < 25 => FireDangerRating.High,
        < 50 => FireDangerRating.VeryHigh,
        < 75 => FireDangerRating.Severe,
        < 100 => FireDangerRating.Extreme,
        _ => FireDangerRating.Catastrophic,
    };

    /// <summary>Head fire rate of spread in km/h: 0.0012 · FFDI · fuel load (t/ha).</summary>
    public static double HeadRateOfSpreadKmh(double ffdi, double fuelLoadTonnesPerHectare) =>
        0.0012 * Math.Max(ffdi, 0) * Math.Max(fuelLoadTonnesPerHectare, 0);

    /// <summary>Length-to-breadth ratio of the spread ellipse from the wind speed (km/h), piecewise as in CSIRO Spark.</summary>
    public static double LengthToBreadthRatio(double windSpeedKmh) => windSpeedKmh switch
    {
        < 5 => 1.0,
        < 25 => 0.9286 * Math.Exp(0.0505 * windSpeedKmh),
        _ => 0.1143 * windSpeedKmh + 0.4143,
    };

    /// <summary>
    /// Backing spread as a fraction of the head spread for an ellipse with the given length-to-breadth ratio:
    /// e = √(1 − LB⁻²), backing = (1 − e) / (1 + e). A circle (LB = 1) spreads equally in all directions.
    /// </summary>
    public static double BackingFraction(double lengthToBreadth)
    {
        var lb = Math.Max(lengthToBreadth, 1.0);
        var e = Math.Sqrt(1.0 - 1.0 / (lb * lb));
        return (1.0 - e) / (1.0 + e);
    }
}
