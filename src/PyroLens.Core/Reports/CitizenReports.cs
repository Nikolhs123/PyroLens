namespace PyroLens.Core.Reports;

/// <summary>What the person saw.</summary>
public enum ReportKind
{
    Smoke,
    Flames,
    Other,
}

public enum ReportStatus
{
    /// <summary>Nobody has looked at it yet.</summary>
    New,

    /// <summary>A dispatcher has seen it (and may have acted on it).</summary>
    Seen,

    /// <summary>Not a fire, a duplicate, or nonsense.</summary>
    Dismissed,
}

/// <summary>Rules for reports from the public, kept out of the API so they are easy to test.</summary>
public static class ReportRules
{
    public const int MaxNoteLength = 500;

    /// <summary>A report this close to a fire counts as being about it.</summary>
    public const double NearbyMeters = 3000;

    public static bool IsValidCoordinate(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    /// <summary>
    /// Trims the note, drops control characters (keeping line breaks as spaces) and collapses runs of spaces.
    /// Returns null for an empty note. Does not shorten: a note that is too long is rejected, not cut.
    /// </summary>
    public static string? CleanNote(string? note)
    {
        if (note is null)
        {
            return null;
        }

        var chars = note.Select(c => c is '\n' or '\r' or '\t' ? ' ' : c).Where(c => !char.IsControl(c));
        var cleaned = string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length == 0 ? null : cleaned;
    }
}
