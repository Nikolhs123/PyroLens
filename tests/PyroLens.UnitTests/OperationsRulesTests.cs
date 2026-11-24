using PyroLens.Core.Incidents;
using PyroLens.Core.Resources;

namespace PyroLens.UnitTests;

public class IncidentLifecycleTests
{
    [Theory]
    [InlineData(IncidentStatus.Detected, IncidentStatus.Confirmed)]
    [InlineData(IncidentStatus.Detected, IncidentStatus.Closed)]      // false alarm
    [InlineData(IncidentStatus.Confirmed, IncidentStatus.Active)]
    [InlineData(IncidentStatus.Active, IncidentStatus.Contained)]
    [InlineData(IncidentStatus.Contained, IncidentStatus.Active)]     // flare-up
    [InlineData(IncidentStatus.Contained, IncidentStatus.Closed)]
    public void Allowed_transitions(IncidentStatus from, IncidentStatus to)
    {
        Assert.True(IncidentLifecycle.CanTransition(from, to));
    }

    [Theory]
    [InlineData(IncidentStatus.Detected, IncidentStatus.Active)]      // must be confirmed first
    [InlineData(IncidentStatus.Active, IncidentStatus.Closed)]        // must be contained first
    [InlineData(IncidentStatus.Closed, IncidentStatus.Active)]        // closed is final
    [InlineData(IncidentStatus.Confirmed, IncidentStatus.Confirmed)]  // not a change
    public void Forbidden_transitions(IncidentStatus from, IncidentStatus to)
    {
        Assert.False(IncidentLifecycle.CanTransition(from, to));
    }

    [Fact]
    public void Every_status_except_closed_has_a_way_forward()
    {
        foreach (var status in Enum.GetValues<IncidentStatus>().Where(s => s != IncidentStatus.Closed))
        {
            Assert.NotEmpty(IncidentLifecycle.NextStatuses(status));
        }

        Assert.Empty(IncidentLifecycle.NextStatuses(IncidentStatus.Closed));
    }

    [Theory]
    [InlineData(IncidentStatus.Detected, false)]
    [InlineData(IncidentStatus.Confirmed, true)]
    [InlineData(IncidentStatus.Active, true)]
    [InlineData(IncidentStatus.Contained, true)]
    [InlineData(IncidentStatus.Closed, false)]
    public void Resources_only_for_confirmed_to_contained(IncidentStatus status, bool accepts)
    {
        Assert.Equal(accepts, IncidentLifecycle.AcceptsResources(status));
    }
}

public class ResourceKindsTests
{
    [Theory]
    [InlineData(ResourceKind.Aircraft, 100_000, 20)]   // 100 km at 300 km/h
    [InlineData(ResourceKind.Helicopter, 100_000, 30)] // 100 km at 200 km/h
    [InlineData(ResourceKind.Engine, 50_000, 78)]      // 50 km × 1.3 by road = 65 km at 50 km/h: exactly 78 min
    [InlineData(ResourceKind.Engine, 50_100, 79)]      // a little further is rounded up
    [InlineData(ResourceKind.Drone, 1_000, 1)]
    [InlineData(ResourceKind.Crew, 0, 0)]
    public void Eta_uses_speed_and_road_factor(ResourceKind kind, double meters, int minutes)
    {
        Assert.Equal(minutes, ResourceKinds.EtaMinutes(kind, meters));
    }

    [Theory]
    [InlineData(ResourceKind.Drone, AirspaceLevel.Low)]
    [InlineData(ResourceKind.Helicopter, AirspaceLevel.High)]
    [InlineData(ResourceKind.Aircraft, AirspaceLevel.High)]
    public void Airborne_units_have_an_airspace_band(ResourceKind kind, AirspaceLevel level)
    {
        Assert.Equal(level, ResourceKinds.Airspace(kind));
    }

    [Theory]
    [InlineData(ResourceKind.Engine)]
    [InlineData(ResourceKind.Crew)]
    public void Ground_units_have_none(ResourceKind kind)
    {
        Assert.Null(ResourceKinds.Airspace(kind));
    }
}
