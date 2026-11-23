using System.Globalization;
using System.Text.Json;

namespace PyroLens.Core.Forecasting;

/// <summary>
/// Parses an Open-Meteo /v1/forecast response requested with
/// hourly=temperature_2m,relative_humidity_2m,wind_speed_10m,wind_direction_10m,wind_gusts_10m and timezone=GMT.
/// One location returns a JSON object; several return an array in request order.
/// </summary>
public static class OpenMeteoParser
{
    public static IReadOnlyList<IReadOnlyList<HourlyWeather>> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
        {
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : "unknown";
            throw new FormatException($"Open-Meteo error: {reason}");
        }

        return root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().Select(ParseLocation).ToList()
            : [ParseLocation(root)];
    }

    private static IReadOnlyList<HourlyWeather> ParseLocation(JsonElement location)
    {
        if (!location.TryGetProperty("hourly", out var hourly))
        {
            throw new FormatException("Open-Meteo response has no 'hourly' block.");
        }

        var time = hourly.GetProperty("time");
        var temperature = hourly.GetProperty("temperature_2m");
        var humidity = hourly.GetProperty("relative_humidity_2m");
        var windSpeed = hourly.GetProperty("wind_speed_10m");
        var windDirection = hourly.GetProperty("wind_direction_10m");
        hourly.TryGetProperty("wind_gusts_10m", out var gusts);

        var result = new List<HourlyWeather>(time.GetArrayLength());
        for (var i = 0; i < time.GetArrayLength(); i++)
        {
            // Hours with a missing value are dropped; the caller checks it got enough hours.
            if (Number(temperature, i) is not { } t ||
                Number(humidity, i) is not { } rh ||
                Number(windSpeed, i) is not { } ws ||
                Number(windDirection, i) is not { } wd)
            {
                continue;
            }

            var at = DateTimeOffset.ParseExact(
                time[i].GetString()!, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            result.Add(new HourlyWeather(at, t, rh, ws, wd, gusts.ValueKind == JsonValueKind.Array ? Number(gusts, i) : null));
        }

        return result;
    }

    private static double? Number(JsonElement array, int index) =>
        index < array.GetArrayLength() && array[index].ValueKind == JsonValueKind.Number ? array[index].GetDouble() : null;
}
