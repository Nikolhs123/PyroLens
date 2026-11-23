namespace PyroLens.Core.Incidents;

/// <summary>
/// Lifecycle of an incident, from «Εντοπίστηκε» (Detected) to «Κλειστό» (Closed).
/// Phase 1 only creates Detected incidents; transitions arrive with the incident state machine.
/// </summary>
public enum IncidentStatus
{
    Detected,
    Confirmed,
    Active,
    Contained,
    Closed,
}
