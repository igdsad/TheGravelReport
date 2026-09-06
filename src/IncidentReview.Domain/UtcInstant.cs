using System.Globalization;
using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Represents a UTC instant with lossless Unix-millisecond precision.</summary>
public sealed record UtcInstant
{
    private const long MinimumUnixMilliseconds = -62_135_596_800_000;
    private const long MaximumUnixMilliseconds = 253_402_300_799_999;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.utc-instant.invalid",
        "The instant must be UTC and map losslessly to a supported Unix millisecond.");

    private UtcInstant(long unixMilliseconds)
    {
        UnixMilliseconds = unixMilliseconds;
    }

    /// <summary>Gets the lossless persistence representation.</summary>
    public long UnixMilliseconds { get; }

    /// <summary>Gets the UTC timestamp.</summary>
    public DateTimeOffset Value => DateTimeOffset.FromUnixTimeMilliseconds(UnixMilliseconds);

    /// <summary>Validates a UTC timestamp read from an untrusted boundary.</summary>
    public static Result<UtcInstant> TryCreate(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero &&
        value.Ticks % TimeSpan.TicksPerMillisecond == 0
            ? Result<UtcInstant>.Success(new UtcInstant(value.ToUnixTimeMilliseconds()))
            : Result<UtcInstant>.Failure(InvalidError);

    /// <summary>Validates a UTC date-time read from an untrusted boundary.</summary>
    public static Result<UtcInstant> TryCreate(DateTime value) =>
        value.Kind == DateTimeKind.Utc &&
        value.Ticks % TimeSpan.TicksPerMillisecond == 0
            ? Result<UtcInstant>.Success(
                new UtcInstant(new DateTimeOffset(value).ToUnixTimeMilliseconds()))
            : Result<UtcInstant>.Failure(InvalidError);

    /// <summary>Validates Unix milliseconds read from an untrusted boundary.</summary>
    public static Result<UtcInstant> TryCreateUnixMilliseconds(long unixMilliseconds) =>
        unixMilliseconds is >= MinimumUnixMilliseconds and <= MaximumUnixMilliseconds
            ? Result<UtcInstant>.Success(new UtcInstant(unixMilliseconds))
            : Result<UtcInstant>.Failure(InvalidError);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("O", CultureInfo.InvariantCulture);
}
