using System.Globalization;
using Microsoft.Extensions.Options;
using PyroLens.Core.Forecasting;

namespace PyroLens.Infrastructure.Weather;

public interface IWeatherProvider
{
    /// <summary>Hourly forecast for each point, starting at the current hour (UTC), in the same order as <paramref name="points"/>.</summary>
    Task<IReadOnlyList<IReadOnlyList<HourlyWeather>>> GetHourlyAsync(IReadOnlyList<GeoPoint> points, int hours, CancellationToken ct);
}

/// <summary>
/// Open-Meteo forecast API: free, no key, non-commercial use, CC BY 4.0 attribution.
/// Several locations go in one request (comma-separated coordinates), BatchSize at a time.
/// </summary>
public sealed class OpenMeteoClient(HttpClient http, IOptions<ForecastOptions> options) : IWeatherProvider
{
    private const string HourlyVariables = "temperature_2m,relative_humidity_2m,wind_speed_10m,wind_direction_10m,wind_gusts_10m";

    public async Task<IReadOnlyList<IReadOnlyList<HourlyWeather>>> GetHourlyAsync(IReadOnlyList<GeoPoint> points, int hours, CancellationToken ct)
    {
        var result = new List<IReadOnlyList<HourlyWeather>>(points.Count);

        foreach (var batch in points.Chunk(options.Value.BatchSize))
        {
            var latitudes = string.Join(',', batch.Select(p => p.Latitude.ToString("F4", CultureInfo.InvariantCulture)));
            var longitudes = string.Join(',', batch.Select(p => p.Longitude.ToString("F4", CultureInfo.InvariantCulture)));
            var url = $"v1/forecast?latitude={latitudes}&longitude={longitudes}&hourly={HourlyVariables}" +
                      $"&forecast_hours={hours}&timezone=GMT&wind_speed_unit=kmh";

            var parsed = OpenMeteoParser.Parse(await http.GetStringAsync(url, ct));
            if (parsed.Count != batch.Length)
            {
                throw new FormatException($"Open-Meteo returned {parsed.Count} locations for {batch.Length} requested.");
            }

            result.AddRange(parsed);
        }

        return result;
    }
}
