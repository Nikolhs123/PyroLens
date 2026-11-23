using PyroLens.Core.Detections;

namespace PyroLens.UnitTests;

public class FirmsCsvParserTests
{
    private const string ViirsHeader =
        "latitude,longitude,bright_ti4,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_ti5,frp,daynight";

    private const string ModisHeader =
        "latitude,longitude,brightness,scan,track,acq_date,acq_time,satellite,confidence,version,bright_t31,frp,daynight";

    [Fact]
    public void Parses_viirs_row()
    {
        var csv = $"{ViirsHeader}\n38.1700,23.7200,345.2,0.39,0.36,2025-08-14,0036,N,VIIRS,h,2.0NRT,298.1,18.4,N\n";

        var result = FirmsCsvParser.Parse(csv);

        var d = Assert.Single(result.Detections);
        Assert.Equal(38.17, d.Latitude);
        Assert.Equal(23.72, d.Longitude);
        Assert.Equal(new DateTimeOffset(2025, 8, 14, 0, 36, 0, TimeSpan.Zero), d.AcquiredAt);
        Assert.Equal(Instrument.Viirs, d.Instrument);
        Assert.Equal("N", d.Satellite);
        Assert.Equal(ConfidenceClass.High, d.ConfidenceClass);
        Assert.Null(d.ConfidencePercent);
        Assert.Equal(345.2, d.BrightnessKelvin);
        Assert.Equal(18.4, d.FrpMegawatts);
        Assert.False(d.IsDaytime);
        Assert.Equal(0, result.SkippedRows);
    }

    [Fact]
    public void Parses_modis_layout_without_instrument_column_using_default()
    {
        var csv = $"{ModisHeader}\r\n37.5,22.1,309.0,1.0,1.0,2025-08-14,1125,Terra,85,6.1NRT,290.2,5.5,D\r\n";

        var d = Assert.Single(FirmsCsvParser.Parse(csv, Instrument.Modis).Detections);

        Assert.Equal(Instrument.Modis, d.Instrument);
        Assert.Equal((short)85, d.ConfidencePercent);
        Assert.Null(d.ConfidenceClass);
        Assert.Equal(309.0, d.BrightnessKelvin);
        Assert.True(d.IsDaytime);
        Assert.Equal(new DateTimeOffset(2025, 8, 14, 11, 25, 0, TimeSpan.Zero), d.AcquiredAt);
    }

    [Theory]
    [InlineData("5", 0, 5)]
    [InlineData("45", 0, 45)]
    [InlineData("130", 1, 30)]
    [InlineData("2359", 23, 59)]
    public void Pads_acq_time_without_leading_zeros(string acqTime, int hour, int minute)
    {
        var csv = $"{ViirsHeader}\n38,23,300,0.4,0.4,2025-08-14,{acqTime},N,VIIRS,n,2.0NRT,290,1,N";

        var d = Assert.Single(FirmsCsvParser.Parse(csv).Detections);

        Assert.Equal(new DateTimeOffset(2025, 8, 14, hour, minute, 0, TimeSpan.Zero), d.AcquiredAt);
    }

    [Theory]
    [InlineData("l", ConfidenceClass.Low)]
    [InlineData("n", ConfidenceClass.Nominal)]
    [InlineData("h", ConfidenceClass.High)]
    [InlineData("nominal", ConfidenceClass.Nominal)]
    [InlineData("HIGH", ConfidenceClass.High)]
    public void Normalises_viirs_confidence(string raw, ConfidenceClass expected)
    {
        var csv = $"{ViirsHeader}\n38,23,300,0.4,0.4,2025-08-14,0100,N,VIIRS,{raw},2.0NRT,290,1,N";

        Assert.Equal(expected, Assert.Single(FirmsCsvParser.Parse(csv).Detections).ConfidenceClass);
    }

    [Theory]
    [InlineData("91,23,300,0.4,0.4,2025-08-14,0100,N,VIIRS,n,2.0NRT,290,1,N")]  // latitude out of range
    [InlineData("38,abc,300,0.4,0.4,2025-08-14,0100,N,VIIRS,n,2.0NRT,290,1,N")] // longitude not a number
    [InlineData("38,23,300,0.4,0.4,2025-13-14,0100,N,VIIRS,n,2.0NRT,290,1,N")]  // bad date
    [InlineData("38,23,300,0.4,0.4,2025-08-14,2575,N,VIIRS,n,2.0NRT,290,1,N")]  // bad time
    [InlineData("38,23,300,0.4,0.4,2025-08-14,0100,N,VIIRS,x,2.0NRT,290,1,N")]  // unknown VIIRS confidence
    [InlineData("38,23,300,0.4,0.4,2025-08-14,0100,A,MODIS,150,6.1NRT,290,1,N")] // MODIS confidence > 100
    [InlineData("38,23,300,0.4,0.4,2025-08-14,0100,X,GOES,n,1,290,1,N")]        // unknown instrument
    public void Skips_invalid_rows_and_counts_them(string badRow)
    {
        var csv = $"{ViirsHeader}\n{badRow}\n38,23,300,0.4,0.4,2025-08-14,0100,N,VIIRS,n,2.0NRT,290,1,N";

        var result = FirmsCsvParser.Parse(csv);

        Assert.Single(result.Detections);
        Assert.Equal(1, result.SkippedRows);
    }

    [Fact]
    public void Ignores_comment_and_blank_lines()
    {
        var csv = $"# synthetic\n\n{ViirsHeader}\n# a comment\n38,23,300,0.4,0.4,2025-08-14,0100,N,VIIRS,n,2.0NRT,290,1,N\n\n";

        var result = FirmsCsvParser.Parse(csv);

        Assert.Single(result.Detections);
        Assert.Equal(0, result.SkippedRows);
    }

    [Fact]
    public void Header_only_yields_no_detections()
    {
        Assert.Empty(FirmsCsvParser.Parse(ViirsHeader).Detections);
        Assert.Empty(FirmsCsvParser.Parse("").Detections);
    }

    [Fact]
    public void Plain_text_error_body_is_rejected()
    {
        var ex = Assert.Throws<FormatException>(() => FirmsCsvParser.Parse("Invalid MAP_KEY."));
        Assert.Contains("Invalid MAP_KEY.", ex.Message);
    }

    [Fact]
    public void Bundled_synthetic_sample_parses_without_errors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "firms_sample_synthetic.csv");

        var result = FirmsCsvParser.Parse(File.ReadAllText(path));

        Assert.Equal(17, result.Detections.Count);
        Assert.Equal(0, result.SkippedRows);
        Assert.Equal(2, result.Detections.Count(d => d.Instrument == Instrument.Modis));
    }
}
