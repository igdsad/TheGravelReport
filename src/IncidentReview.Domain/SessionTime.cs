using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Represents a non-negative, millisecond-precise position in a session.</summary>
public sealed record SessionTime
{
    private const long MaximumMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.session-time.invalid",
        "Session time must be a finite, non-negative whole number of milliseconds within the supported range.");

    private SessionTime(long milliseconds)
    {
        Milliseconds = milliseconds;
    }

    /// <summary>Gets the lossless persistence and replay representation.</summary>
    public long Milliseconds { get; }

    /// <summary>Gets the corresponding duration.</summary>
    public TimeSpan Value => TimeSpan.FromTicks(Milliseconds * TimeSpan.TicksPerMillisecond);

    /// <summary>Validates a duration read from an untrusted boundary.</summary>
    public static Result<SessionTime> TryCreate(TimeSpan value) =>
        value.Ticks >= 0 && value.Ticks % TimeSpan.TicksPerMillisecond == 0
            ? Result<SessionTime>.Success(
                new SessionTime(value.Ticks / TimeSpan.TicksPerMillisecond))
            : Result<SessionTime>.Failure(InvalidError);

    /// <summary>Validates integer milliseconds read from an untrusted boundary.</summary>
    public static Result<SessionTime> TryCreateMilliseconds(long milliseconds) =>
        milliseconds is >= 0 and <= MaximumMilliseconds
            ? Result<SessionTime>.Success(new SessionTime(milliseconds))
            : Result<SessionTime>.Failure(InvalidError);

    /// <summary>Validates numeric milliseconds without silently rounding them.</summary>
    public static Result<SessionTime> TryCreateMilliseconds(double milliseconds) =>
        double.IsFinite(milliseconds) &&
        milliseconds >= 0 &&
        milliseconds <= MaximumMilliseconds &&
        milliseconds == Math.Truncate(milliseconds)
            ? Result<SessionTime>.Success(new SessionTime((long)milliseconds))
            : Result<SessionTime>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Milliseconds.ToString(CultureInfo.InvariantCulture);
}
