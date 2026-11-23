using PyroLens.Core.Forecasting;

namespace PyroLens.UnitTests;

public class OpenMeteoParserTests
{
    // Trimmed from a real response on 23 Nov 2026 (two locations).
    private const string TwoLocations = """
        [{"latitude":39.66412,"longitude":22.67862,"timezone":"GMT","hourly_units":{"time":"iso8601"},
          "hourly":{"time":["2026-11-23T22:00","2026-11-23T23:00"],"temperature_2m":[12.0,11.7],"relative_humidity_2m":[62,55],
                    "wind_speed_10m":[13.4,12.4],"wind_direction_10m":[85,82],"wind_gusts_10m":[23.8,24.5]}},
         {"latitude":38.16292,"longitude":23.652271,"timezone":"GMT","location_id":1,"hourly_units":{"time":"iso8601"},
          "hourly":{"time":["2026-11-23T22:00","2026-11-23T23:00"],"temperature_2m":[8.2,8.1],"relative_humidity_2m":[61,62],
                    "wind_speed_10m":[23.2,20.3],"wind_direction_10m":[277,277],"wind_gusts_10m":[67.3,66.6]}}]
        """;

    [Fact]
    public void Parses_several_locations_in_request_order()
    {
        var result = OpenMeteoParser.Parse(TwoLocations);

        Assert.Equal(2, result.Count);
        var first = result[0][0];
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 22, 0, 0, TimeSpan.Zero), first.Time);
        Assert.Equal(12.0, first.TemperatureC);
        Assert.Equal(62, first.RelativeHumidityPercent);
        Assert.Equal(13.4, first.WindSpeedKmh);
        Assert.Equal(85, first.WindFromDegrees);
        Assert.Equal(23.8, first.WindGustKmh);
        Assert.Equal(277, result[1][1].WindFromDegrees);
    }

    [Fact]
    public void Parses_a_single_location_object()
    {
        const string one = """
            {"latitude":38.0,"longitude":23.0,"hourly":{"time":["2026-11-23T22:00"],"temperature_2m":[9.5],
             "relative_humidity_2m":[70],"wind_speed_10m":[5.0],"wind_direction_10m":[180]}}
            """;

        var result = OpenMeteoParser.Parse(one);

        var hour = Assert.Single(Assert.Single(result));
        Assert.Null(hour.WindGustKmh);
    }

    [Fact]
    public void Drops_hours_with_missing_values()
    {
        const string gap = """
            {"hourly":{"time":["2026-11-23T22:00","2026-11-23T23:00"],"temperature_2m":[null,9.0],
             "relative_humidity_2m":[70,71],"wind_speed_10m":[5.0,6.0],"wind_direction_10m":[180,190]}}
            """;

        var hour = Assert.Single(Assert.Single(OpenMeteoParser.Parse(gap)));

        Assert.Equal(23, hour.Time.Hour);
    }

    [Fact]
    public void Error_body_is_reported()
    {
        var ex = Assert.Throws<FormatException>(() => OpenMeteoParser.Parse("""{"error":true,"reason":"Latitude must be in range of -90 to 90°."}"""));

        Assert.Contains("Latitude must be in range", ex.Message);
    }
}
