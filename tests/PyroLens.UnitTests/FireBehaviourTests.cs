using PyroLens.Core.Forecasting;

namespace PyroLens.UnitTests;

public class FireBehaviourTests
{
    [Fact]
    public void Ffdi_matches_the_mk5_equation()
    {
        // 2·exp(−0.45 + 0.987·ln10 − 0.0345·20 + 0.0338·30 + 0.0234·20) = 27.32
        var ffdi = FireBehaviour.ForestFireDangerIndex(temperatureC: 30, relativeHumidityPercent: 20, windSpeedKmh: 20, droughtFactor: 10);

        Assert.Equal(27.32, ffdi, 2);
    }

    [Fact]
    public void Ffdi_rises_with_heat_wind_and_dryness_and_falls_with_humidity()
    {
        var baseline = FireBehaviour.ForestFireDangerIndex(25, 40, 15, 7);

        Assert.True(FireBehaviour.ForestFireDangerIndex(35, 40, 15, 7) > baseline);
        Assert.True(FireBehaviour.ForestFireDangerIndex(25, 40, 40, 7) > baseline);
        Assert.True(FireBehaviour.ForestFireDangerIndex(25, 40, 15, 10) > baseline);
        Assert.True(FireBehaviour.ForestFireDangerIndex(25, 80, 15, 7) < baseline);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(11)]
    public void Ffdi_rejects_drought_factor_outside_1_to_10(double droughtFactor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FireBehaviour.ForestFireDangerIndex(25, 40, 15, droughtFactor));
    }

    [Theory]
    [InlineData(0, FireDangerRating.LowModerate)]
    [InlineData(11.9, FireDangerRating.LowModerate)]
    [InlineData(12, FireDangerRating.High)]
    [InlineData(25, FireDangerRating.VeryHigh)]
    [InlineData(50, FireDangerRating.Severe)]
    [InlineData(75, FireDangerRating.Extreme)]
    [InlineData(100, FireDangerRating.Catastrophic)]
    public void Ratings_follow_the_mcarthur_bands(double ffdi, FireDangerRating expected)
    {
        Assert.Equal(expected, FireBehaviour.Rate(ffdi));
    }

    [Fact]
    public void Rate_of_spread_is_0_0012_times_ffdi_times_fuel_load()
    {
        Assert.Equal(0.6, FireBehaviour.HeadRateOfSpreadKmh(ffdi: 50, fuelLoadTonnesPerHectare: 10), 10);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(4.9, 1.0)]
    [InlineData(20, 2.5496)]   // 0.9286·e^(0.0505·20)
    [InlineData(40, 4.9863)]   // 0.1143·40 + 0.4143
    public void Length_to_breadth_grows_with_wind(double windKmh, double expected)
    {
        Assert.Equal(expected, FireBehaviour.LengthToBreadthRatio(windKmh), 3);
    }

    [Fact]
    public void Calm_fire_is_a_circle_and_windy_fire_barely_backs()
    {
        Assert.Equal(1.0, FireBehaviour.BackingFraction(1.0), 10);
        Assert.InRange(FireBehaviour.BackingFraction(4.0), 0.015, 0.02);
    }
}
