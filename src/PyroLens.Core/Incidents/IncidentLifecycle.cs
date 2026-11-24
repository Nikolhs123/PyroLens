namespace PyroLens.Core.Incidents;

/// <summary>
/// Which status may follow which. Detected → Confirmed → Active → Contained → Closed, with false alarms closed
/// early and flare-ups taking a contained incident back to Active. Closed is final: new detections at the same
/// place open a new incident.
/// </summary>
public static class IncidentLifecycle
{
    private static readonly Dictionary<IncidentStatus, IncidentStatus[]> Next = new()
    {
        [IncidentStatus.Detected] = [IncidentStatus.Confirmed, IncidentStatus.Closed],
        [IncidentStatus.Confirmed] = [IncidentStatus.Active, IncidentStatus.Closed],
        [IncidentStatus.Active] = [IncidentStatus.Contained],
        [IncidentStatus.Contained] = [IncidentStatus.Active, IncidentStatus.Closed],
        [IncidentStatus.Closed] = [],
    };

    public static IReadOnlyList<IncidentStatus> NextStatuses(IncidentStatus status) => Next[status];

    public static bool CanTransition(IncidentStatus from, IncidentStatus to) => Next[from].Contains(to);

    /// <summary>Statuses in which crews, vehicles and airspace can be committed to the incident.</summary>
    public static bool AcceptsResources(IncidentStatus status) =>
        status is IncidentStatus.Confirmed or IncidentStatus.Active or IncidentStatus.Contained;

    /// <summary>A dispatcher is working the incident: it stays on the board until closed, even without new detections.</summary>
    public static bool IsManaged(IncidentStatus status) =>
        status is IncidentStatus.Confirmed or IncidentStatus.Active or IncidentStatus.Contained;
}
