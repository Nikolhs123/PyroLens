using System.Globalization;

namespace PyroLens.Core.Detections;

public sealed record FirmsParseResult(IReadOnlyList<RawDetection> Detections, int SkippedRows);

/// <summary>
/// Parses the CSV returned by the FIRMS area API (MODIS and VIIRS products).
/// Columns are located by header name, so both product layouts are accepted.
/// Lines starting with '#' are treated as comments.
/// </summary>
public static class FirmsCsvParser
{
    public static FirmsParseResult Parse(string csv, Instrument? defaultInstrument = null)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var lines = csv
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        if (lines.Count == 0)
        {
            return new FirmsParseResult([], 0);
        }

        var header = lines[0].Split(',');
        if (!header.Contains("latitude", StringComparer.OrdinalIgnoreCase))
        {
            // FIRMS answers errors such as an invalid MAP_KEY with a plain text body.
            throw new FormatException($"Not a FIRMS CSV response: '{Truncate(lines[0], 120)}'");
        }

        var col = new Columns(header);
        var detections = new List<RawDetection>(lines.Count - 1);
        var skipped = 0;

        foreach (var line in lines.Skip(1))
        {
            if (TryParseRow(line.Split(','), col, defaultInstrument, out var detection))
            {
                detections.Add(detection);
            }
            else
            {
                skipped++;
            }
        }

        return new FirmsParseResult(detections, skipped);
    }

    private static bool TryParseRow(string[] f, Columns c, Instrument? defaultInstrument, out RawDetection detection)
    {
        detection = null!;

        if (!TryDouble(Get(f, c.Latitude), out var lat) || lat is < -90 or > 90 ||
            !TryDouble(Get(f, c.Longitude), out var lon) || lon is < -180 or > 180 ||
            !TryAcquiredAt(Get(f, c.AcqDate), Get(f, c.AcqTime), out var acquiredAt))
        {
            return false;
        }

        Instrument instrument;
        var instrumentText = Get(f, c.Instrument);
        if (instrumentText.Equals("MODIS", StringComparison.OrdinalIgnoreCase))
        {
            instrument = Instrument.Modis;
        }
        else if (instrumentText.Equals("VIIRS", StringComparison.OrdinalIgnoreCase))
        {
            instrument = Instrument.Viirs;
        }
        else if (defaultInstrument is { } d)
        {
            instrument = d;
        }
        else
        {
            return false;
        }

        var confidenceRaw = Get(f, c.Confidence);
        ConfidenceClass? confidenceClass = null;
        short? confidencePercent = null;

        if (instrument == Instrument.Modis)
        {
            if (!short.TryParse(confidenceRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct) ||
                pct is < 0 or > 100)
            {
                return false;
            }

            confidencePercent = pct;
        }
        else
        {
            confidenceClass = confidenceRaw.ToLowerInvariant() switch
            {
                "l" or "low" => ConfidenceClass.Low,
                "n" or "nominal" => ConfidenceClass.Nominal,
                "h" or "high" => ConfidenceClass.High,
                _ => null,
            };

            if (confidenceClass is null)
            {
                return false;
            }
        }

        var dayNight = Get(f, c.DayNight);

        detection = new RawDetection(
            Latitude: lat,
            Longitude: lon,
            AcquiredAt: acquiredAt,
            Instrument: instrument,
            Satellite: Get(f, c.Satellite),
            ConfidenceRaw: confidenceRaw,
            ConfidenceClass: confidenceClass,
            ConfidencePercent: confidencePercent,
            BrightnessKelvin: TryDouble(Get(f, c.Brightness), out var b) ? b : null,
            FrpMegawatts: TryDouble(Get(f, c.Frp), out var frp) ? frp : null,
            IsDaytime: dayNight switch
            {
                "D" => true,
                "N" => false,
                _ => null,
            });

        return true;
    }

    private static bool TryAcquiredAt(string date, string time, out DateTimeOffset value)
    {
        value = default;

        // acq_time is HHMM in UTC; leading zeros may be missing ("5" means 00:05).
        if (time.Length is 0 or > 4 || !time.All(char.IsAsciiDigit))
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
            $"{date} {time.PadLeft(4, '0')}",
            "yyyy-MM-dd HHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }

    private static bool TryDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Get(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed class Columns
    {
        public Columns(string[] header)
        {
            int Find(params string[] names) =>
                Array.FindIndex(header, h => names.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase));

            Latitude = Find("latitude");
            Longitude = Find("longitude");
            AcqDate = Find("acq_date");
            AcqTime = Find("acq_time");
            Satellite = Find("satellite");
            Instrument = Find("instrument");
            Confidence = Find("confidence");
            Brightness = Find("bright_ti4", "brightness");
            Frp = Find("frp");
            DayNight = Find("daynight");
        }

        public int Latitude { get; }
        public int Longitude { get; }
        public int AcqDate { get; }
        public int AcqTime { get; }
        public int Satellite { get; }
        public int Instrument { get; }
        public int Confidence { get; }
        public int Brightness { get; }
        public int Frp { get; }
        public int DayNight { get; }
    }
}
