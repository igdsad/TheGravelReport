using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>A validated signed offset from a recorded incident's replay position.</summary>
public sealed record ReplayOffset
{
    /// <summary>Largest supported offset in either direction.</summary>
    public const long MaximumAbsoluteMilliseconds = 60_000;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.replay-offset.invalid",
        "A replay offset must be a whole number of milliseconds between -60000 and 60000.");

    private ReplayOffset(long milliseconds)
    {
        Milliseconds = milliseconds;
    }

    /// <summary>An offset that targets the incident itself.</summary>
    public static ReplayOffset Zero { get; } = new(0);

    /// <summary>Gets the signed number of milliseconds from the incident.</summary>
    public long Milliseconds { get; }

    /// <summary>Creates a signed, millisecond-precise offset.</summary>
    public static Result<ReplayOffset> TryCreateMilliseconds(long milliseconds) =>
        milliseconds is >= -MaximumAbsoluteMilliseconds and <= MaximumAbsoluteMilliseconds
            ? Result<ReplayOffset>.Success(new ReplayOffset(milliseconds))
            : Result<ReplayOffset>.Failure(InvalidError);

    /// <summary>Applies this offset and clamps positions before the session start to zero.</summary>
    public Result<SessionTime> ApplyTo(SessionTime sessionTime)
    {
        ArgumentNullException.ThrowIfNull(sessionTime);
        var target = Milliseconds < 0 && sessionTime.Milliseconds < -Milliseconds
            ? 0
            : sessionTime.Milliseconds + Milliseconds;
        return SessionTime.TryCreateMilliseconds(target);
    }

    /// <inheritdoc />
    public override string ToString() => Milliseconds.ToString(CultureInfo.InvariantCulture);
}
