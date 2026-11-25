namespace PyroLens.Infrastructure.Persistence;

/// <summary>A row of <c>job_runs</c>: when a background job last started, finished and succeeded.</summary>
public sealed class JobRun
{
    public required string Job { get; set; }
    public DateTimeOffset LastStartedAt { get; set; }
    public required string LastStartedBy { get; set; }
    public DateTimeOffset? LastFinishedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }

    /// <summary>Started and not finished yet, unless it has been going so long that its process must have died.</summary>
    public bool IsRunning(DateTimeOffset now, TimeSpan stuckAfter) =>
        (LastFinishedAt is null || LastFinishedAt < LastStartedAt) && LastStartedAt > now - stuckAfter;
}
