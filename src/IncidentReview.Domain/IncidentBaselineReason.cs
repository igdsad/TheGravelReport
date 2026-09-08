namespace IncidentReview.Domain;

/// <summary>Explains why incident-counter progress must establish a new baseline.</summary>
public sealed record IncidentBaselineReason
{
    private readonly string _name;

    private IncidentBaselineReason(string name)
    {
        _name = name;
    }

    /// <summary>Gets the reason used when no durable checkpoint exists.</summary>
    public static IncidentBaselineReason Initial { get; } = new(nameof(Initial));

    /// <summary>Gets the reason used when the application session changes.</summary>
    public static IncidentBaselineReason SessionChanged { get; } = new(nameof(SessionChanged));

    /// <summary>Gets the reason used when a new heat changes replay session number.</summary>
    public static IncidentBaselineReason HeatChanged { get; } = new(nameof(HeatChanged));

    /// <summary>Gets the reason used when a cumulative counter decreases.</summary>
    public static IncidentBaselineReason CounterReset { get; } = new(nameof(CounterReset));

    /// <inheritdoc />
    public override string ToString() => _name;
}
