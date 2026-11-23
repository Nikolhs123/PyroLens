namespace PyroLens.Core.Forecasting;

/// <summary>Forecast weather for one hour, starting at <see cref="Time"/> (UTC).</summary>
public sealed record HourlyWeather(
    DateTimeOffset Time,
    double TemperatureC,
    double RelativeHumidityPercent,
    double WindSpeedKmh,
    double WindFromDegrees,
    double? WindGustKmh);

public readonly record struct GeoPoint(double Latitude, double Longitude);

public sealed record SpreadSettings(double DroughtFactor, double FuelLoadTonnesPerHectare);

/// <summary>Where a fire could reach after <see cref="HorizonHours"/> hours, and the weather that drives it.</summary>
public sealed record HorizonForecast(
    int HorizonHours,
    DateTimeOffset WeatherFrom,
    double MeanTemperatureC,
    double MeanRelativeHumidityPercent,
    double MeanWindSpeedKmh,
    double MeanWindFromDegrees,
    double? MaxWindGustKmh,
    double PeakFfdi,
    FireDangerRating Rating,
    double PeakRateOfSpreadMetersPerHour,
    double HeadDistanceMeters,
    double LengthToBreadth,
    double SpreadBearingDegrees,
    IReadOnlyList<GeoPoint> Ellipse);

/// <summary>
/// Turns hourly weather into spread ellipses. Each hour adds its own head run in its own downwind direction;
/// the ellipse uses the total run, the overall direction of travel and the mean wind speed.
/// </summary>
public static class SpreadForecaster
{
    private const int EllipseVertices = 48;
    private const double MinimumHeadDistanceMeters = 1;

    public static HorizonForecast Forecast(GeoPoint origin, IReadOnlyList<HourlyWeather> hours, int horizonHours, SpreadSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(horizonHours, 1);
        if (hours.Count < horizonHours)
        {
            throw new ArgumentException($"Need {horizonHours} hours of weather, got {hours.Count}.", nameof(hours));
        }

        var window = hours.Take(horizonHours).ToList();

        double runEast = 0, runNorth = 0, head = 0, peakFfdi = 0, peakRos = 0;
        double windEast = 0, windNorth = 0;

        foreach (var hour in window)
        {
            var ffdi = FireBehaviour.ForestFireDangerIndex(hour.TemperatureC, hour.RelativeHumidityPercent, hour.WindSpeedKmh, settings.DroughtFactor);
            var rosMetersPerHour = FireBehaviour.HeadRateOfSpreadKmh(ffdi, settings.FuelLoadTonnesPerHectare) * 1000;
            var downwind = ToRadians(hour.WindFromDegrees + 180);

            head += rosMetersPerHour;
            runEast += rosMetersPerHour * Math.Sin(downwind);
            runNorth += rosMetersPerHour * Math.Cos(downwind);
            peakFfdi = Math.Max(peakFfdi, ffdi);
            peakRos = Math.Max(peakRos, rosMetersPerHour);

            var from = ToRadians(hour.WindFromDegrees);
            windEast += hour.WindSpeedKmh * Math.Sin(from);
            windNorth += hour.WindSpeedKmh * Math.Cos(from);
        }

        var meanWind = window.Average(h => h.WindSpeedKmh);
        var meanWindFrom = windEast == 0 && windNorth == 0 ? window[0].WindFromDegrees : ToDegrees(Math.Atan2(windEast, windNorth));
        var bearing = runEast == 0 && runNorth == 0 ? Normalize(window[0].WindFromDegrees + 180) : ToDegrees(Math.Atan2(runEast, runNorth));
        var lengthToBreadth = FireBehaviour.LengthToBreadthRatio(meanWind);

        return new HorizonForecast(
            HorizonHours: horizonHours,
            WeatherFrom: window[0].Time,
            MeanTemperatureC: window.Average(h => h.TemperatureC),
            MeanRelativeHumidityPercent: window.Average(h => h.RelativeHumidityPercent),
            MeanWindSpeedKmh: meanWind,
            MeanWindFromDegrees: Normalize(meanWindFrom),
            MaxWindGustKmh: window.Max(h => h.WindGustKmh),
            PeakFfdi: peakFfdi,
            Rating: FireBehaviour.Rate(peakFfdi),
            PeakRateOfSpreadMetersPerHour: peakRos,
            HeadDistanceMeters: head,
            LengthToBreadth: lengthToBreadth,
            SpreadBearingDegrees: Normalize(bearing),
            Ellipse: Ellipse(origin, head, lengthToBreadth, bearing));
    }

    /// <summary>
    /// A closed ring of points: the head is <paramref name="headDistanceMeters"/> from the origin along
    /// <paramref name="bearingDegrees"/>, the back reaches upwind by the backing fraction, the width is length / LB.
    /// Uses a local flat-earth approximation, accurate to well under 1 % for the few kilometres involved.
    /// </summary>
    public static IReadOnlyList<GeoPoint> Ellipse(GeoPoint origin, double headDistanceMeters, double lengthToBreadth, double bearingDegrees)
    {
        var head = Math.Max(headDistanceMeters, MinimumHeadDistanceMeters);
        var lb = Math.Max(lengthToBreadth, 1.0);
        var back = head * FireBehaviour.BackingFraction(lb);
        var semiMajor = (head + back) / 2;
        var semiMinor = semiMajor / lb;
        var centreOffset = semiMajor - back;

        var bearing = ToRadians(bearingDegrees);
        var (sinB, cosB) = Math.SinCos(bearing);
        var metersPerDegreeLat = 111_320.0;
        var metersPerDegreeLon = 111_320.0 * Math.Cos(ToRadians(origin.Latitude));

        var ring = new List<GeoPoint>(EllipseVertices + 1);
        for (var i = 0; i < EllipseVertices; i++)
        {
            var t = 2 * Math.PI * i / EllipseVertices;
            var along = centreOffset + semiMajor * Math.Cos(t); // metres downwind of the origin
            var across = semiMinor * Math.Sin(t);               // metres to the right of the direction of travel

            var east = along * sinB + across * cosB;
            var north = along * cosB - across * sinB;

            ring.Add(new GeoPoint(origin.Latitude + north / metersPerDegreeLat, origin.Longitude + east / metersPerDegreeLon));
        }

        ring.Add(ring[0]);
        return ring;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static double ToDegrees(double radians) => radians * 180 / Math.PI;

    private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;
}
