using PyroLens.Core.Forecasting;

namespace PyroLens.UnitTests;

public class SpreadForecasterTests
{
    private static readonly GeoPoint Parnitha = new(38.17, 23.72);
    private static readonly SpreadSettings Summer = new(DroughtFactor: 10, FuelLoadTonnesPerHectare: 12);
    private static readonly DateTimeOffset Start = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private const double MetersPerDegree = 111_320.0;

    [Fact]
    public void Three_hours_of_constant_weather_run_three_times_as_far_as_one()
    {
        var hours = Constant(hours: 3, windFrom: 0, windKmh: 30);

        var one = SpreadForecaster.Forecast(Parnitha, hours, 1, Summer);
        var three = SpreadForecaster.Forecast(Parnitha, hours, 3, Summer);

        Assert.Equal(3 * one.HeadDistanceMeters, three.HeadDistanceMeters, 6);
        Assert.Equal(Start, three.WeatherFrom);
        Assert.Equal(one.PeakFfdi, three.PeakFfdi, 10);
    }

    [Fact]
    public void North_wind_pushes_the_fire_south()
    {
        var forecast = SpreadForecaster.Forecast(Parnitha, Constant(1, windFrom: 0, windKmh: 30), 1, Summer);

        Assert.Equal(180, forecast.SpreadBearingDegrees, 6);

        var southmost = forecast.Ellipse.Min(p => p.Latitude);
        var northmost = forecast.Ellipse.Max(p => p.Latitude);
        var expectedBack = forecast.HeadDistanceMeters * FireBehaviour.BackingFraction(forecast.LengthToBreadth);

        // The head reaches the full run downwind; the back only creeps a little upwind.
        Assert.Equal(Parnitha.Latitude - forecast.HeadDistanceMeters / MetersPerDegree, southmost, 5);
        Assert.Equal(Parnitha.Latitude + expectedBack / MetersPerDegree, northmost, 5);
    }

    [Fact]
    public void Ellipse_width_is_length_divided_by_length_to_breadth()
    {
        var forecast = SpreadForecaster.Forecast(Parnitha, Constant(1, windFrom: 270, windKmh: 40), 1, Summer); // west wind -> runs east

        var lengthMeters = (forecast.Ellipse.Max(p => p.Longitude) - forecast.Ellipse.Min(p => p.Longitude))
            * MetersPerDegree * Math.Cos(Parnitha.Latitude * Math.PI / 180);
        var widthMeters = (forecast.Ellipse.Max(p => p.Latitude) - forecast.Ellipse.Min(p => p.Latitude)) * MetersPerDegree;

        Assert.Equal(90, forecast.SpreadBearingDegrees, 6);
        Assert.Equal(forecast.LengthToBreadth, lengthMeters / widthMeters, 2);
    }

    [Fact]
    public void Ellipse_is_a_closed_ring()
    {
        var forecast = SpreadForecaster.Forecast(Parnitha, Constant(1, 45, 20), 1, Summer);

        Assert.Equal(forecast.Ellipse[0], forecast.Ellipse[^1]);
        Assert.True(forecast.Ellipse.Count > 4);
    }

    [Fact]
    public void Turning_wind_bends_the_overall_direction()
    {
        HourlyWeather[] hours =
        [
            Hour(0, windFrom: 0, windKmh: 30),   // pushes south
            Hour(1, windFrom: 0, windKmh: 30),
            Hour(2, windFrom: 270, windKmh: 30), // pushes east
        ];

        var forecast = SpreadForecaster.Forecast(Parnitha, hours, 3, Summer);

        Assert.InRange(forecast.SpreadBearingDegrees, 140, 160); // mostly south, somewhat east
    }

    [Fact]
    public void Too_few_hours_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => SpreadForecaster.Forecast(Parnitha, Constant(2, 0, 10), 3, Summer));
    }

    private static HourlyWeather[] Constant(int hours, double windFrom, double windKmh) =>
        Enumerable.Range(0, hours).Select(h => Hour(h, windFrom, windKmh)).ToArray();

    private static HourlyWeather Hour(int offset, double windFrom, double windKmh) =>
        new(Start.AddHours(offset), TemperatureC: 34, RelativeHumidityPercent: 18, windKmh, windFrom, WindGustKmh: windKmh * 1.6);
}
