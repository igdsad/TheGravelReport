namespace IncidentReview.Store.Sqlite;

internal sealed class PreferencesRow
{
    public long ReplayLeadInMilliseconds { get; init; }

    public long AutoPause { get; init; }

    public double PlaybackSpeed { get; init; }

    public string? PreferredCamera { get; init; }

    public int ThemePreference { get; init; }

    public string? SubmitterName { get; init; }

    public required string CustomEventKey { get; init; }

    public string? EventJoinCode { get; init; }
}

internal sealed class StoreOperationRow
{
    public required string CommandKind { get; init; }

    public int CommandVersion { get; init; }

    public required byte[] PayloadFingerprint { get; init; }
}
