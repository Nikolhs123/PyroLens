using PyroLens.Core.Forecasting;
using PyroLens.Core.Incidents;
using PyroLens.Core.Reports;

namespace PyroLens.UnitTests;

public sealed class ReportRulesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  smoke   behind the\nchurch \t ", "smoke behind the church")]
    [InlineData("big\u0007 fire", "big fire")]
    public void Notes_are_tidied(string? note, string? expected) => Assert.Equal(expected, ReportRules.CleanNote(note));

    [Fact]
    public void Long_notes_are_not_cut() => Assert.Equal(600, ReportRules.CleanNote(new string('a', 600))!.Length);

    [Theory]
    [InlineData(38.0, 23.7, true)]
    [InlineData(91, 23.7, false)]
    [InlineData(38.0, -181, false)]
    [InlineData(double.NaN, 23.7, false)]
    public void Coordinates_must_be_on_earth(double lat, double lon, bool valid) => Assert.Equal(valid, ReportRules.IsValidCoordinate(lat, lon));

    [Fact]
    public void Reports_nearby_are_worth_a_look()
    {
        var facts = new AttentionFacts(IncidentStatus.Detected, 0, 0, FireDangerRating.LowModerate, null, 0, null, null, null, RecentReports: 2);

        var flag = Assert.Single(AttentionRules.Evaluate(facts, DateTimeOffset.UnixEpoch, AttentionSettings.Default));

        Assert.Equal(new Attention(AttentionRule.Reported, AttentionLevel.Watch, WithinHours: 6, Reports: 2), flag);
    }
}
