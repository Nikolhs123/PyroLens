using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PyroLens.Core.Incidents;
using PyroLens.Core.Reports;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Operations;
using PyroLens.Infrastructure.Persistence;
using PyroLens.Infrastructure.Reports;

namespace PyroLens.IntegrationTests;

/// <summary>Reports from the public, against PostGIS with the sample's three incidents.</summary>
[Collection(PostgisCollection.Name)]
public sealed class ReportTests(PostgisFixture postgis)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(37.98, 23.73, true)]   // Athens
    [InlineData(35.34, 25.13, true)]   // Heraklion, Crete
    [InlineData(37.925, 23.62, true)]  // 1 km off Piraeus: inside the 2 km sea buffer
    [InlineData(42.70, 23.32, false)]  // Sofia
    [InlineData(36.00, 20.00, false)]  // open Ionian Sea
    public async Task Only_places_in_Greece_are_accepted(double lat, double lon, bool accepted)
    {
        await using var env = await CreateAsync();

        var result = await env.Reports.SubmitAsync(lat, lon, ReportKind.Smoke, null, default);

        Assert.Equal(accepted ? Outcome.Ok : Outcome.Invalid, result.Outcome);
        Assert.Equal(accepted ? 1 : 0, await env.Db.Reports.CountAsync());
    }

    [Fact]
    public async Task A_report_knows_the_fire_nearest_to_it()
    {
        await using var env = await CreateAsync();
        var parnitha = await env.Db.Incidents.SingleAsync(i => i.Id == env.Parnitha);

        // About 1.1 km north of the fire's centre, and one in central Athens far from any fire.
        var near = await env.Reports.SubmitAsync(parnitha.Centroid.Y + 0.01, parnitha.Centroid.X, ReportKind.Flames, "  big  flames ", default);
        var far = await env.Reports.SubmitAsync(37.98, 23.73, ReportKind.Other, null, default);

        Assert.Equal(env.Parnitha, near.Value!.NearestIncidentId);
        Assert.InRange(near.Value.NearestIncidentMeters!.Value, 1000, 1300);
        Assert.Equal("big flames", near.Value.Note);
        Assert.Null(far.Value!.NearestIncidentId);
        Assert.Equal(ReportStatus.New, far.Value.Status);
    }

    [Fact]
    public async Task Long_notes_are_refused_not_cut()
    {
        await using var env = await CreateAsync();

        var result = await env.Reports.SubmitAsync(37.98, 23.73, ReportKind.Smoke, new string('x', 501), default);

        Assert.Equal(Outcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Review_moves_forward_only()
    {
        await using var env = await CreateAsync();
        var id = (await env.Reports.SubmitAsync(37.98, 23.73, ReportKind.Smoke, null, default)).Value!.Id;

        var seen = await env.Reports.ReviewAsync(id, ReportStatus.Seen, "Nick", default);
        Assert.Equal(Outcome.Ok, seen.Outcome);
        Assert.Equal("Nick", seen.Value!.ReviewedBy);
        Assert.Equal(Now, seen.Value.ReviewedAt);

        Assert.Equal(Outcome.Conflict, (await env.Reports.ReviewAsync(id, ReportStatus.Seen, "Maria", default)).Outcome);
        Assert.Equal(Outcome.Ok, (await env.Reports.ReviewAsync(id, ReportStatus.Dismissed, "Maria", default)).Outcome);
        Assert.Equal(Outcome.Conflict, (await env.Reports.ReviewAsync(id, ReportStatus.Seen, "Nick", default)).Outcome);
        Assert.Equal(Outcome.Invalid, (await env.Reports.ReviewAsync(id, ReportStatus.New, "Nick", default)).Outcome);
        Assert.Equal(Outcome.NotFound, (await env.Reports.ReviewAsync(999, ReportStatus.Seen, "Nick", default)).Outcome);

        // Dismissed reports leave the list unless asked for.
        Assert.Empty(await env.Reports.ListAsync(Now.AddDays(-1), includeDismissed: false, default));
        Assert.Single(await env.Reports.ListAsync(Now.AddDays(-1), includeDismissed: true, default));
    }

    [Fact]
    public async Task A_report_near_a_fire_flags_it_until_dismissed_or_old()
    {
        await using var env = await CreateAsync();
        var fire = await env.Db.Incidents.SingleAsync(i => i.Id == env.Smallest);
        var report = (await env.Reports.SubmitAsync(fire.Centroid.Y + 0.005, fire.Centroid.X, ReportKind.Smoke, null, default)).Value!;

        var flag = Assert.Single(await env.FlagsAsync(env.Smallest));
        Assert.Equal(AttentionRule.Reported, flag.Rule);
        Assert.Equal(1, flag.Reports);

        await env.Reports.ReviewAsync(report.Id, ReportStatus.Dismissed, "Nick", default);
        Assert.Empty(await env.FlagsAsync(env.Smallest));

        // A second report, then seven hours: no longer recent.
        await env.Reports.SubmitAsync(fire.Centroid.Y, fire.Centroid.X + 0.005, ReportKind.Smoke, null, default);
        Assert.Single(await env.FlagsAsync(env.Smallest));
        env.Clock.Advance(TimeSpan.FromHours(7));
        Assert.Empty(await env.FlagsAsync(env.Smallest));
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
        var bySize = await root.ServiceProvider.GetRequiredService<PyroLensDbContext>().Incidents.AsNoTracking()
            .OrderByDescending(i => i.DetectionCount).Select(i => i.Id).ToListAsync();
        return new Env(services, root, clock, bySize[0], bySize[2]);
    }

    private sealed class Env(ServiceProvider services, AsyncServiceScope scope, MutableClock clock, long parnitha, long smallest) : IAsyncDisposable
    {
        public MutableClock Clock { get; } = clock;
        public long Parnitha { get; } = parnitha;
        public long Smallest { get; } = smallest;
        public PyroLensDbContext Db => scope.ServiceProvider.GetRequiredService<PyroLensDbContext>();
        public ReportService Reports => scope.ServiceProvider.GetRequiredService<ReportService>();

        public async Task<IReadOnlyList<Attention>> FlagsAsync(long incident)
        {
            await using var s = services.CreateAsyncScope();
            return (await s.ServiceProvider.GetRequiredService<AttentionService>().EvaluateAsync([incident], default))[incident];
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
