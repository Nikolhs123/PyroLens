using NetTopologySuite.Geometries;
using NpgsqlTypes;
using PyroLens.Core.Incidents;
using PyroLens.Core.Resources;

namespace PyroLens.Infrastructure.Persistence;

public enum IncidentEventType
{
    Created,
    StatusChanged,
    ResourceReserved,
    ResourceAssigned,
    ResourceReleased,
    AirspaceReserved,
    AirspaceCancelled,
}

public sealed class IncidentEvent
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public IncidentEventType Type { get; set; }
    public required string Actor { get; set; }
    public IncidentStatus? FromStatus { get; set; }
    public IncidentStatus? ToStatus { get; set; }
    public string? Note { get; set; }

    /// <summary>JSON object with event-specific facts (callsign, reservation id, …).</summary>
    public string Details { get; set; } = "{}";
}

public sealed class Resource
{
    public long Id { get; set; }
    public required string Callsign { get; set; }
    public ResourceKind Kind { get; set; }
    public required string BaseName { get; set; }
    public Point BaseLocation { get; set; } = null!;
    public ResourceStatus Status { get; set; }
    public long? IncidentId { get; set; }
    public DateTimeOffset? ReservedUntil { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AirspaceReservation
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public AirspaceLevel Level { get; set; }
    public Polygon Area { get; set; } = null!;
    public double RadiusMeters { get; set; }
    /// <summary>[start, end) in UTC.</summary>
    public NpgsqlRange<DateTime> During { get; set; }
    public required string ReservedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}
