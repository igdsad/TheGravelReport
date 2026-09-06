using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>
/// Captures the stable identity and optional display context of an incident participant.
/// </summary>
public sealed record IncidentParticipant
{
    /// <summary>Gets the maximum display-field length in UTF-16 code units.</summary>
    public const int MaximumDisplayTextLength = 128;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-participant.invalid",
        "An incident participant requires an identity and valid bounded display context.");

    private IncidentParticipant(
        ParticipantIdentity identity,
        string? driverName,
        string? teamName,
        string? carNumber)
    {
        Identity = identity;
        DriverName = driverName;
        TeamName = teamName;
        CarNumber = carNumber;
    }

    /// <summary>Gets the stable participant identity within the session.</summary>
    public ParticipantIdentity Identity { get; }

    /// <summary>Gets the normalized driver display name when available.</summary>
    public string? DriverName { get; }

    /// <summary>Gets the normalized team display name when available.</summary>
    public string? TeamName { get; }

    /// <summary>Gets the normalized car number when available.</summary>
    public string? CarNumber { get; }

    /// <summary>Validates and snapshots participant identity and optional display context.</summary>
    public static Result<IncidentParticipant> TryCreate(
        ParticipantIdentity? identity,
        string? driverName,
        string? teamName,
        string? carNumber)
    {
        if (identity is null ||
            !TryNormalizeDisplayText(driverName, out var normalizedDriverName) ||
            !TryNormalizeDisplayText(teamName, out var normalizedTeamName) ||
            !TryNormalizeDisplayText(carNumber, out var normalizedCarNumber))
        {
            return Result<IncidentParticipant>.Failure(InvalidError);
        }

        return Result<IncidentParticipant>.Success(new IncidentParticipant(
            identity,
            normalizedDriverName,
            normalizedTeamName,
            normalizedCarNumber));
    }

    private static bool TryNormalizeDisplayText(string? value, out string? normalized) =>
        DomainText.TryNormalizeOptional(
            value,
            MaximumDisplayTextLength,
            allowMultiline: false,
            out normalized);
}
