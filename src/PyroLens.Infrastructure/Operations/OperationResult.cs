namespace PyroLens.Infrastructure.Operations;

public enum Outcome
{
    Ok,
    NotFound,

    /// <summary>The world changed since the caller looked (taken, expired, already moved on): reload and retry.</summary>
    Conflict,

    /// <summary>The request can never succeed as asked (wrong transition, bad window).</summary>
    Invalid,
}

/// <summary>Result of a dispatcher action; the API maps the outcome to 200/404/409/422.</summary>
public sealed record OperationResult<T>(Outcome Outcome, T? Value, string? Message)
{
    public static OperationResult<T> Success(T value) => new(Outcome.Ok, value, null);

    public static OperationResult<T> Missing(string message) => new(Outcome.NotFound, default, message);

    public static OperationResult<T> Conflicted(string message) => new(Outcome.Conflict, default, message);

    public static OperationResult<T> Rejected(string message) => new(Outcome.Invalid, default, message);
}

public sealed class OperationsOptions
{
    public const string Section = "Operations";

    /// <summary>How long a reservation holds a resource before anyone else may take it.</summary>
    public TimeSpan ReservationHold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Longest airspace window one reservation may cover.</summary>
    public TimeSpan MaxAirspaceWindow { get; set; } = TimeSpan.FromHours(12);
}
