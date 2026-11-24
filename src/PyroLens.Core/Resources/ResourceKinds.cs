namespace PyroLens.Core.Resources;

public enum ResourceKind
{
    /// <summary>Fire engine / water tender (ground).</summary>
    Engine,

    /// <summary>Hand crew on foot with a transport vehicle (ground).</summary>
    Crew,

    Helicopter,

    /// <summary>Fixed-wing water bomber.</summary>
    Aircraft,

    /// <summary>Reconnaissance drone.</summary>
    Drone,
}

public enum ResourceStatus
{
    Available,
    Reserved,
    Assigned,
    OutOfService,
}

public enum AirspaceLevel
{
    /// <summary>Up to about 120 m above ground: drones.</summary>
    Low,

    /// <summary>Above that: helicopters and aircraft.</summary>
    High,
}

/// <summary>Rough planning figures for the simulated fleet. Not real dispatch data.</summary>
public static class ResourceKinds
{
    /// <summary>Ground units do not drive in straight lines; this stretches the straight-line distance.</summary>
    public const double RoadFactor = 1.3;

    public static double CruiseSpeedKmh(ResourceKind kind) => kind switch
    {
        ResourceKind.Aircraft => 300,
        ResourceKind.Helicopter => 200,
        ResourceKind.Drone => 60,
        ResourceKind.Engine or ResourceKind.Crew => 50,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool IsAirborne(ResourceKind kind) => kind is ResourceKind.Aircraft or ResourceKind.Helicopter or ResourceKind.Drone;

    /// <summary>Airspace band the resource flies in, or null for ground units.</summary>
    public static AirspaceLevel? Airspace(ResourceKind kind) => kind switch
    {
        ResourceKind.Drone => AirspaceLevel.Low,
        ResourceKind.Aircraft or ResourceKind.Helicopter => AirspaceLevel.High,
        _ => null,
    };

    /// <summary>Minutes from base to the incident: straight line for aircraft, stretched by <see cref="RoadFactor"/> on the ground.</summary>
    public static int EtaMinutes(ResourceKind kind, double straightLineMeters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(straightLineMeters);
        var meters = IsAirborne(kind) ? straightLineMeters : straightLineMeters * RoadFactor;
        // Round away floating-point noise first, or exactly 78 min (78.00000000000001) would become 79.
        return (int)Math.Ceiling(Math.Round(meters / 1000 / CruiseSpeedKmh(kind) * 60, 6));
    }
}
