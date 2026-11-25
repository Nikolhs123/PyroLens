using PyroLens.Core.Forecasting;
using PyroLens.Core.Incidents;

namespace PyroLens.UnitTests;

public sealed class AttentionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly AttentionSettings Settings = AttentionSettings.Default;

    private static AttentionFacts Facts(
        IncidentStatus status = IncidentStatus.Detected,
        int recentPixels = 0,
        int pixelsSinceContained = 0,
        FireDangerRating? latest = null,
        FireDangerRating? previous = null,
        int assigned = 0,
        DateTimeOffset? managedSince = null,
        DateTimeOffset? containedAt = null,
        DateTimeOffset? lastReleasedAt = null) =>
        new(status, recentPixels, pixelsSinceContained, latest, previous, assigned, managedSince, containedAt, lastReleasedAt);

    private static IReadOnlyList<Attention> Evaluate(AttentionFacts facts) => AttentionRules.Evaluate(facts, Now, Settings);

    [Fact]
    public void A_quiet_fire_needs_nothing() => Assert.Empty(Evaluate(Facts(latest: FireDangerRating.High, previous: FireDangerRating.High)));

    [Fact]
    public void Closed_fires_are_never_flagged() =>
        Assert.Empty(Evaluate(Facts(IncidentStatus.Closed, recentPixels: 9, latest: FireDangerRating.Extreme)));

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(90, true)]
    public void Confirmed_with_no_units_becomes_urgent_after_20_minutes(int minutes, bool flagged)
    {
        var flags = Evaluate(Facts(IncidentStatus.Confirmed, managedSince: Now.AddMinutes(-minutes)));

        if (flagged)
        {
            var flag = Assert.Single(flags);
            Assert.Equal(new Attention(AttentionRule.NoUnits, AttentionLevel.Urgent, Since: Now.AddMinutes(-minutes)), flag);
        }
        else
        {
            Assert.Empty(flags);
        }
    }

    [Fact]
    public void A_unit_on_the_fire_clears_the_no_units_flag() =>
        Assert.Empty(Evaluate(Facts(IncidentStatus.Active, assigned: 1, managedSince: Now.AddHours(-3))));

    [Fact]
    public void Releasing_the_last_unit_restarts_the_clock()
    {
        var managed = Now.AddHours(-3);

        Assert.Empty(Evaluate(Facts(IncidentStatus.Active, managedSince: managed, lastReleasedAt: Now.AddMinutes(-5))));

        var flag = Assert.Single(Evaluate(Facts(IncidentStatus.Active, managedSince: managed, lastReleasedAt: Now.AddMinutes(-25))));
        Assert.Equal(Now.AddMinutes(-25), flag.Since);
    }

    [Fact]
    public void New_detections_are_not_waiting_for_units() =>
        Assert.Empty(Evaluate(Facts(IncidentStatus.Detected, managedSince: null)));

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Three_new_pixels_in_six_hours_means_spreading(int pixels, bool flagged)
    {
        var flags = Evaluate(Facts(IncidentStatus.Detected, recentPixels: pixels));

        if (flagged)
        {
            Assert.Equal(new Attention(AttentionRule.Spreading, AttentionLevel.Watch, Pixels: pixels, WithinHours: 6), Assert.Single(flags));
        }
        else
        {
            Assert.Empty(flags);
        }
    }

    [Fact]
    public void A_contained_fire_seen_burning_again_is_urgent()
    {
        var contained = Now.AddHours(-2);

        var flag = Assert.Single(Evaluate(Facts(IncidentStatus.Contained, recentPixels: 5, pixelsSinceContained: 2, containedAt: contained)));

        Assert.Equal(new Attention(AttentionRule.BurningAgain, AttentionLevel.Urgent, Since: contained, Pixels: 2), flag);
    }

    [Fact]
    public void Pixels_from_before_containment_do_not_count() =>
        Assert.Empty(Evaluate(Facts(IncidentStatus.Contained, recentPixels: 5, pixelsSinceContained: 0, containedAt: Now.AddHours(-1))));

    [Theory]
    [InlineData(FireDangerRating.LowModerate, FireDangerRating.High, AttentionLevel.Watch, true)]
    [InlineData(FireDangerRating.High, FireDangerRating.Severe, AttentionLevel.Urgent, true)]
    [InlineData(FireDangerRating.VeryHigh, FireDangerRating.VeryHigh, AttentionLevel.Urgent, false)]
    [InlineData(null, FireDangerRating.Extreme, AttentionLevel.Urgent, false)]
    public void Danger_that_is_very_high_or_rising_is_flagged(FireDangerRating? previous, FireDangerRating latest, AttentionLevel level, bool rose)
    {
        var flag = Assert.Single(Evaluate(Facts(latest: latest, previous: previous)));

        Assert.Equal(new Attention(AttentionRule.Danger, level, From: rose ? previous : null, To: latest), flag);
    }

    [Fact]
    public void Falling_danger_below_very_high_is_not_flagged() =>
        Assert.Empty(Evaluate(Facts(latest: FireDangerRating.High, previous: FireDangerRating.Severe)));

    [Fact]
    public void Several_reasons_are_all_listed()
    {
        var flags = Evaluate(Facts(
            IncidentStatus.Active, recentPixels: 4, latest: FireDangerRating.Severe, managedSince: Now.AddMinutes(-45)));

        Assert.Equal([AttentionRule.NoUnits, AttentionRule.Spreading, AttentionRule.Danger], flags.Select(f => f.Rule));
    }
}
