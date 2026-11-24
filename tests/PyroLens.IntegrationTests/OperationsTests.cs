using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PyroLens.Core.Incidents;
using PyroLens.Core.Resources;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Operations;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.IntegrationTests;

/// <summary>
/// Dispatcher actions against PostGIS: lifecycle, the append-only timeline, resource reservations under
/// concurrency, and airspace reservations enforced by the exclusion constraint.
/// The sample gives three incidents: Parnitha (largest), North Evia and Rhodes.
/// </summary>
[Collection(PostgisCollection.Name)]
public sealed class OperationsTests(PostgisFixture postgis)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 6, 0, 0, TimeSpan.Zero);
    private const string Actor = "test";

    // ---------- Lifecycle ----------

    [Fact]
    public async Task Status_changes_follow_the_lifecycle_and_land_in_the_timeline()
    {
        await using var env = await CreateAsync();

        var confirmed = await env.Incidents.ChangeStatusAsync(env.Parnitha, IncidentStatus.Detected, IncidentStatus.Confirmed, Actor, "Seen from the lookout", default);
        var active = await env.Incidents.ChangeStatusAsync(env.Parnitha, IncidentStatus.Confirmed, IncidentStatus.Active, Actor, null, default);

        Assert.Equal(Outcome.Ok, confirmed.Outcome);
        Assert.Equal(IncidentStatus.Active, active.Value!.Status);

        var timeline = await env.Incidents.TimelineAsync(env.Parnitha, default);
        Assert.Equal(
            [IncidentEventType.Created, IncidentEventType.StatusChanged, IncidentEventType.StatusChanged],
            timeline.Select(e => e.Type));
        Assert.Equal("Seen from the lookout", timeline[1].Note);
        Assert.Equal((IncidentStatus.Confirmed, IncidentStatus.Active), (timeline[2].FromStatus, timeline[2].ToStatus));
    }

    [Fact]
    public async Task A_stale_status_change_is_a_conflict()
    {
        await using var env = await CreateAsync();

        await env.Incidents.ChangeStatusAsync(env.Parnitha, IncidentStatus.Detected, IncidentStatus.Confirmed, "dispatcher A", null, default);
        // Dispatcher B still sees "detected" on screen.
        var late = await env.Incidents.ChangeStatusAsync(env.Parnitha, IncidentStatus.Detected, IncidentStatus.Closed, "dispatcher B", null, default);

        Assert.Equal(Outcome.Conflict, late.Outcome);
        Assert.Contains("already Confirmed", late.Message);
    }

    [Fact]
    public async Task Invalid_transitions_and_unknown_incidents_are_refused()
    {
        await using var env = await CreateAsync();

        var skip = await env.Incidents.ChangeStatusAsync(env.Parnitha, IncidentStatus.Detected, IncidentStatus.Active, Actor, null, default);
        var missing = await env.Incidents.ChangeStatusAsync(999_999, IncidentStatus.Detected, IncidentStatus.Confirmed, Actor, null, default);

        Assert.Equal(Outcome.Invalid, skip.Outcome);
        Assert.Equal(Outcome.NotFound, missing.Outcome);
    }

    [Fact]
    public async Task The_timeline_cannot_be_edited_or_deleted()
    {
        await using var env = await CreateAsync();

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            env.Db.Database.ExecuteSqlRawAsync("UPDATE incident_events SET actor = 'someone else'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            env.Db.Database.ExecuteSqlRawAsync("DELETE FROM incident_events"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, update.SqlState);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, delete.SqlState);
    }

    // ---------- Resources ----------

    [Fact]
    public async Task Resources_need_a_confirmed_incident()
    {
        await using var env = await CreateAsync();

        var result = await env.Resources.ReserveAsync(await env.ResourceId("HEL-01"), env.Parnitha, Actor, default);

        Assert.Equal(Outcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Reserve_assign_release()
    {
        await using var env = await CreateAsync();
        await env.Confirm(env.Parnitha);
        var heli = await env.ResourceId("HEL-01");

        var reserved = await env.Resources.ReserveAsync(heli, env.Parnitha, Actor, default);
        Assert.Equal(ResourceStatus.Reserved, reserved.Value!.Status);
        Assert.Equal(Now.AddMinutes(5), reserved.Value.ReservedUntil);

        var assigned = await env.Resources.AssignAsync(heli, env.Parnitha, Actor, default);
        Assert.Equal(ResourceStatus.Assigned, assigned.Value!.Status);
        Assert.Null(assigned.Value.ReservedUntil);

        var released = await env.Resources.ReleaseAsync(heli, env.Parnitha, Actor, default);
        Assert.Equal(Outcome.Ok, released.Outcome);
        Assert.Equal(ResourceStatus.Available, (await env.Db.Resources.AsNoTracking().SingleAsync(r => r.Id == heli)).Status);

        var types = (await env.Incidents.TimelineAsync(env.Parnitha, default)).Select(e => e.Type).ToList();
        Assert.Equal(
            [IncidentEventType.ResourceReserved, IncidentEventType.ResourceAssigned, IncidentEventType.ResourceReleased],
            types.Skip(2));
    }

    [Fact]
    public async Task A_held_unit_is_taken_only_after_its_hold_expires()
    {
        await using var env = await CreateAsync();
        await env.Confirm(env.Parnitha);
        await env.Confirm(env.Evia);
        var engine = await env.ResourceId("ENG-11");

        Assert.Equal(Outcome.Ok, (await env.Resources.ReserveAsync(engine, env.Parnitha, Actor, default)).Outcome);

        var tooEarly = await env.Resources.ReserveAsync(engine, env.Evia, Actor, default);
        Assert.Equal(Outcome.Conflict, tooEarly.Outcome);
        Assert.Contains($"reserved for incident {env.Parnitha}", tooEarly.Message);

        env.Clock.Advance(TimeSpan.FromMinutes(6));

        var tooLate = await env.Resources.AssignAsync(engine, env.Parnitha, Actor, default);
        Assert.Equal(Outcome.Conflict, tooLate.Outcome);

        var takenOver = await env.Resources.ReserveAsync(engine, env.Evia, Actor, default);
        Assert.Equal(Outcome.Ok, takenOver.Outcome);
        Assert.Equal(env.Evia, takenOver.Value!.IncidentId);
    }

    [Fact]
    public async Task Twenty_concurrent_reservations_have_exactly_one_winner()
    {
        await using var env = await CreateAsync();
        await env.Confirm(env.Parnitha);
        await env.Confirm(env.Evia);
        var aircraft = await env.ResourceId("AIR-01");

        // Each request has its own scope, DbContext and connection, like separate HTTP requests.
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            await using var scope = env.Services.CreateAsyncScope();
            var ops = scope.ServiceProvider.GetRequiredService<ResourceOperations>();
            return await ops.ReserveAsync(aircraft, i % 2 == 0 ? env.Parnitha : env.Evia, $"dispatcher {i}", default);
        }));

        Assert.Equal(1, results.Count(r => r.Outcome == Outcome.Ok));
        Assert.Equal(19, results.Count(r => r.Outcome == Outcome.Conflict));
        Assert.Equal(1, await env.Db.Events.CountAsync(e => e.Type == IncidentEventType.ResourceReserved));
    }

    [Fact]
    public async Task Suggestions_are_the_fastest_free_units()
    {
        await using var env = await CreateAsync();
        await env.Confirm(env.Parnitha);
        var hel01 = await env.ResourceId("HEL-01");
        await env.Resources.ReserveAsync(hel01, env.Parnitha, Actor, default);

        var suggestions = (await env.Resources.SuggestAsync(env.Parnitha, 5, default)).Value!;

        // HEL-01 (Tatoi, ~9 km) is taken; its twin HEL-02 is next, then the Elefsina aircraft (~19 km at 300 km/h).
        Assert.Equal("HEL-02", suggestions[0].Resource.Callsign);
        Assert.Equal(3, suggestions[0].EtaMinutes);
        Assert.DoesNotContain(suggestions, s => s.Resource.Id == hel01);
        Assert.Equal(suggestions.Select(s => s.EtaMinutes).Order(), suggestions.Select(s => s.EtaMinutes));
    }

    // ---------- Airspace ----------

    [Fact]
    public async Task Overlapping_airspace_in_the_same_band_is_refused_by_the_database()
    {
        await using var env = await CreateAsync();
        await env.Confirm(env.Parnitha);

        var first = await env.Airspace.ReserveAsync(env.Parnitha, AirspaceLevel.High, 3000, Now, Now.AddHours(2), Actor, default);
        Assert.Equal(Outcome.Ok, first.Outcome);

        var overlapping = await env.Airspace.ReserveAsync(env.Parnitha, AirspaceLevel.High, 2000, Now.AddHours(1), Now.AddHours(3), Actor, default);
        Assert.Equal(Outcome.Conflict, overlapping.Outcome);
        Assert.Contains($"reservation {first.Value!.Id}", overlapping.Message);

        // Other band, or the next window ([start, end) ranges touch without overlapping): allowed.
        Assert.Equal(Outcome.Ok, (await env.Airspace.ReserveAsync(env.Parnitha, AirspaceLevel.Low, 3000, Now, Now.AddHours(2), Actor, default)).Outcome);
        Assert.Equal(Outcome.Ok, (await env.Airspace.ReserveAsync(env.Parnitha, AirspaceLevel.High, 3000, Now.AddHours(2), Now.AddHours(4), Actor, default)).Outcome);
    }

    [Fact]
    public async Task Ten_concurrent_overlapping_reservations_have_exactly_one_winner()
    {
        await using var env = await CreateAsync();
        await env.Confirm(env.Parnitha);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
        {
            await using var scope = env.Services.CreateAsyncScope();
            var ops = scope.ServiceProvider.GetRequiredService<AirspaceOperations>();
            return await ops.ReserveAsync(env.Parnitha, AirspaceLevel.High, 5000, Now, Now.AddHours(1), $"dispatcher {i}", default);
        }));

        Assert.Equal(1, results.Count(r => r.Outcome == Outcome.Ok));
        Assert.Equal(9, results.Count(r => r.Outcome == Outcome.Conflict));
    }

    [Fact]
    public async Task A_cancelled_reservation_frees_the_airspace()
    {
        await using var env = await CreateAsync();
        await env.Confirm(env.Parnitha);

        var first = await env.Airspace.ReserveAsync(env.Parnitha, AirspaceLevel.Low, 1000, Now, Now.AddHours(1), Actor, default);
        await env.Airspace.CancelAsync(first.Value!.Id, Actor, default);

        Assert.Equal(Outcome.Ok, (await env.Airspace.ReserveAsync(env.Parnitha, AirspaceLevel.Low, 1000, Now, Now.AddHours(1), Actor, default)).Outcome);
        Assert.Equal(Outcome.Conflict, (await env.Airspace.CancelAsync(first.Value.Id, Actor, default)).Outcome);
    }

    [Fact]
    public async Task Closing_an_incident_releases_its_units_and_airspace()
    {
        await using var env = await CreateAsync();
        await env.Confirm(env.Parnitha);
        var drone = await env.ResourceId("DRN-01");
        await env.Resources.ReserveAsync(drone, env.Parnitha, Actor, default);
        await env.Resources.AssignAsync(drone, env.Parnitha, Actor, default);
        await env.Airspace.ReserveAsync(env.Parnitha, AirspaceLevel.Low, 2000, Now, Now.AddHours(3), Actor, default);

        await env.Incidents.ChangeStatusAsync(env.Parnitha, IncidentStatus.Confirmed, IncidentStatus.Closed, Actor, "False alarm", default);

        Assert.Equal(ResourceStatus.Available, (await env.Db.Resources.AsNoTracking().SingleAsync(r => r.Id == drone)).Status);
        Assert.Empty(await env.Airspace.ListActiveAsync(env.Parnitha, default));
        Assert.Equal(Outcome.Invalid, (await env.Resources.ReserveAsync(drone, env.Parnitha, Actor, default)).Outcome);
    }

    // ---------- Setup ----------

    private async Task<Env> CreateAsync()
    {
        var clock = new MutableClock(Now);
        var services = await postgis.CreateServicesAsync(Now, s => s.AddSingleton<TimeProvider>(clock));
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IngestionService>().RunOnceAsync(default);
        }

        var root = services.CreateAsyncScope();
        var db = root.ServiceProvider.GetRequiredService<PyroLensDbContext>();
        var byCount = await db.Incidents.AsNoTracking().OrderByDescending(i => i.DetectionCount).Select(i => i.Id).ToListAsync();
        return new Env(services, root, clock, byCount[0], byCount[1]);
    }

    private sealed class Env(ServiceProvider services, AsyncServiceScope scope, MutableClock clock, long parnitha, long evia) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;
        public MutableClock Clock { get; } = clock;
        public long Parnitha { get; } = parnitha;
        public long Evia { get; } = evia;
        public PyroLensDbContext Db => scope.ServiceProvider.GetRequiredService<PyroLensDbContext>();
        public IncidentOperations Incidents => scope.ServiceProvider.GetRequiredService<IncidentOperations>();
        public ResourceOperations Resources => scope.ServiceProvider.GetRequiredService<ResourceOperations>();
        public AirspaceOperations Airspace => scope.ServiceProvider.GetRequiredService<AirspaceOperations>();

        public Task<long> ResourceId(string callsign) => Db.Resources.Where(r => r.Callsign == callsign).Select(r => r.Id).SingleAsync();

        public async Task Confirm(long incident)
        {
            var result = await Incidents.ChangeStatusAsync(incident, IncidentStatus.Detected, IncidentStatus.Confirmed, Actor, null, default);
            Assert.Equal(Outcome.Ok, result.Outcome);
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await Services.DisposeAsync();
        }
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
