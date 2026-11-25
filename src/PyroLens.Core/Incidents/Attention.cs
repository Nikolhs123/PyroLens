using PyroLens.Core.Forecasting;

namespace PyroLens.Core.Incidents;

/// <summary>Why a fire needs a dispatcher's attention.</summary>
public enum AttentionRule
{
    /// <summary>Confirmed or active, and no unit has been on it for too long.</summary>
    NoUnits,

    /// <summary>Several new satellite pixels in the last few hours.</summary>
    Spreading,

    /// <summary>Marked contained, but satellites still see it burning.</summary>
    BurningAgain,

    /// <summary>Fire danger is very high or worse, or rose since the previous forecast.</summary>
    Danger,
}

public enum AttentionLevel
{
    /// <summary>Worth a look.</summary>
    Watch,

    /// <summary>Someone should act now.</summary>
    Urgent,
}

/// <summary>One reason a fire needs attention. Only the fields that matter for its rule are set.</summary>
/// <param name="Since">NoUnits: since when no unit has been on it. BurningAgain: when it was marked contained.</param>
/// <param name="Pixels">Spreading and BurningAgain: how many new satellite pixels.</param>
/// <param name="WithinHours">Spreading: the window the pixels were counted in.</param>
/// <param name="From">Danger: the previous forecast's rating, when it rose.</param>
/// <param name="To">Danger: the current rating.</param>
public sealed record Attention(
    AttentionRule Rule,
    AttentionLevel Level,
    DateTimeOffset? Since = null,
    int? Pixels = null,
    int? WithinHours = null,
    FireDangerRating? From = null,
    FireDangerRating? To = null);

/// <summary>What the rules look at for one incident, gathered by a single query.</summary>
/// <param name="RecentPixels">Satellite pixels acquired in the last <see cref="AttentionSettings.SpreadingWindow"/>.</param>
/// <param name="PixelsSinceContained">Pixels acquired after the incident was last marked contained.</param>
/// <param name="LatestRating">Danger rating of the newest +3 h forecast.</param>
/// <param name="PreviousRating">Danger rating of the forecast before it.</param>
/// <param name="AssignedUnits">Units sent to it right now (a hold does not count).</param>
/// <param name="ManagedSince">When a dispatcher last took it on: confirmed it, or reported a flare-up.</param>
/// <param name="ContainedAt">When it was last marked contained.</param>
/// <param name="LastReleasedAt">When its last unit was released.</param>
public sealed record AttentionFacts(
    IncidentStatus Status,
    int RecentPixels,
    int PixelsSinceContained,
    FireDangerRating? LatestRating,
    FireDangerRating? PreviousRating,
    int AssignedUnits,
    DateTimeOffset? ManagedSince,
    DateTimeOffset? ContainedAt,
    DateTimeOffset? LastReleasedAt);

public sealed record AttentionSettings(TimeSpan NoUnitsAfter, int SpreadingPixels, TimeSpan SpreadingWindow)
{
    public static readonly AttentionSettings Default = new(TimeSpan.FromMinutes(20), 3, TimeSpan.FromHours(6));

    /// <summary>From this rating up, danger alone makes a fire urgent.</summary>
    public const FireDangerRating UrgentDanger = FireDangerRating.VeryHigh;
}

/// <summary>
/// Escalation rules: plain conditions over facts the database already has, so they need no job and no state.
/// A flag disappears by itself once its condition is gone (a unit is sent, the pixels stop, the danger drops).
/// </summary>
public static class AttentionRules
{
    public static IReadOnlyList<Attention> Evaluate(AttentionFacts f, DateTimeOffset now, AttentionSettings s)
    {
        var flags = new List<Attention>();
        if (f.Status == IncidentStatus.Closed)
        {
            return flags;
        }

        // Confirmed or active with nobody on it. The clock starts when a dispatcher took it on,
        // or again when its last unit was released.
        if (f.Status is IncidentStatus.Confirmed or IncidentStatus.Active && f.AssignedUnits == 0 && f.ManagedSince is { } managed)
        {
            var since = f.LastReleasedAt is { } released && released > managed ? released : managed;
            if (now - since >= s.NoUnitsAfter)
            {
                flags.Add(new Attention(AttentionRule.NoUnits, AttentionLevel.Urgent, Since: since));
            }
        }

        // Contained fires should be quiet: any new pixel is a possible flare-up.
        if (f.Status == IncidentStatus.Contained)
        {
            if (f.PixelsSinceContained > 0)
            {
                flags.Add(new Attention(AttentionRule.BurningAgain, AttentionLevel.Urgent, Since: f.ContainedAt, Pixels: f.PixelsSinceContained));
            }
        }
        else if (f.RecentPixels >= s.SpreadingPixels)
        {
            flags.Add(new Attention(
                AttentionRule.Spreading, AttentionLevel.Watch, Pixels: f.RecentPixels, WithinHours: (int)Math.Round(s.SpreadingWindow.TotalHours)));
        }

        if (f.LatestRating is { } latest)
        {
            var rose = f.PreviousRating is { } previous && latest > previous;
            if (latest >= AttentionSettings.UrgentDanger)
            {
                flags.Add(new Attention(AttentionRule.Danger, AttentionLevel.Urgent, From: rose ? f.PreviousRating : null, To: latest));
            }
            else if (rose)
            {
                flags.Add(new Attention(AttentionRule.Danger, AttentionLevel.Watch, From: f.PreviousRating, To: latest));
            }
        }

        return flags;
    }
}
