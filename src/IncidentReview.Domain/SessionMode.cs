using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Describes whether a simulator session is live or replayed.</summary>
public sealed record SessionMode
{
    private const int LiveValue = 1;
    private const int ReplayValue = 2;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.session-mode.invalid",
        "The session mode code is unknown.");

    private SessionMode(int value)
    {
        Value = value;
    }

    /// <summary>Gets live-session mode. Its stable persisted value is 1.</summary>
    public static SessionMode Live { get; } = new(LiveValue);

    /// <summary>Gets replay-session mode. Its stable persisted value is 2.</summary>
    public static SessionMode Replay { get; } = new(ReplayValue);

    /// <summary>Gets the stable persisted integer code.</summary>
    public int Value { get; }

    /// <summary>Validates a persisted integer code.</summary>
    public static Result<SessionMode> TryCreate(int value) => value switch
    {
        LiveValue => Result<SessionMode>.Success(Live),
        ReplayValue => Result<SessionMode>.Success(Replay),
        _ => Result<SessionMode>.Failure(InvalidError),
    };

    /// <inheritdoc />
    public override string ToString() => Value == LiveValue ? nameof(Live) : nameof(Replay);
}
