namespace PyroLens.IntegrationTests;

/// <summary>A clock the test moves by hand, e.g. to let a reservation expire or a job become due.</summary>
public sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
