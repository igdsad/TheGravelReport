namespace IncidentReview.Store.Sqlite;

internal class SessionRow
{
    public required string SessionId { get; init; }
    public required string Simulator { get; init; }
    public string? SimulatorSessionKey { get; init; }
    public int IdentityKind { get; init; }
    public int SimulatorSessionNumber { get; init; }
    public int SessionMode { get; init; }
    public long StartedAtUnixMilliseconds { get; init; }
}

internal sealed class SessionSummaryRow : SessionRow
{
    public long IncidentCount { get; init; }
    public long PendingIncidentCount { get; init; }
}

internal sealed class IncidentRow
{
    public required string IncidentId { get; init; }
    public required string SessionId { get; init; }
    public int ReplaySessionNumber { get; init; }
    public long ReplaySessionTimeMilliseconds { get; init; }
    public long ObservedAtUnixMilliseconds { get; init; }
    public int IncidentPointsDelta { get; init; }
    public int IncidentPointsTotal { get; init; }
    public int CounterEpoch { get; init; }
    public int? Lap { get; init; }
    public double? LapDistance { get; init; }
    public int ReviewStatus { get; init; }
    public int? Classification { get; init; }
    public string? Notes { get; init; }
    public long CreatedAtUnixMilliseconds { get; init; }
    public long UpdatedAtUnixMilliseconds { get; init; }
}

internal sealed class IncidentCheckpointRow
{
    public required string SessionId { get; init; }
    public int CounterEpoch { get; init; }
    public int LastIncidentPointsTotal { get; init; }
    public int LastReplaySessionNumber { get; init; }
    public long LastReplaySessionTimeMilliseconds { get; init; }
    public long UpdatedAtUnixMilliseconds { get; init; }
}
