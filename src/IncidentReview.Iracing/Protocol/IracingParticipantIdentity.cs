using System.Globalization;

namespace IncidentReview.Iracing.Protocol;

internal static class IracingParticipantIdentity
{
    private const string CarIndexSegment = "car-index";
    private const string TeamSegment = "team";
    private const string UserSegment = "user";
    private const string DriverUserSegment = "driver-user";

    public static string CreateTeamScoped(int carIndex, int teamId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{CarIndexSegment}:{carIndex}:{TeamSegment}:{teamId}");

    public static string CreateUserScoped(int carIndex, int userId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{CarIndexSegment}:{carIndex}:{UserSegment}:{userId}");

    public static string CreateDriverScoped(int carIndex, int? teamId, int userId) =>
        teamId is > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{CarIndexSegment}:{carIndex}:{TeamSegment}:{teamId.Value}:{DriverUserSegment}:{userId}")
            : CreateUserScoped(carIndex, userId);

    public static bool TrySelectCounter(
        int carIndex,
        string canonicalIdentity,
        int? teamId,
        int? userId,
        bool hasValidTeamIncidentCount,
        int? teamIncidentCount,
        bool hasCurrentDriverIncidentCountField,
        bool hasValidCurrentDriverIncidentCount,
        int? currentDriverIncidentCount,
        out string identity,
        out int? incidentCount,
        out IracingIncidentCounterSource counterSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalIdentity);

        if (hasCurrentDriverIncidentCountField)
        {
            if (userId is not > 0)
            {
                identity = canonicalIdentity;
                incidentCount = null;
                counterSource = IracingIncidentCounterSource.CurrentDriver;
                return false;
            }

            identity = CreateDriverScoped(carIndex, teamId, userId.Value);
            incidentCount = hasValidCurrentDriverIncidentCount &&
                currentDriverIncidentCount >= 0
                ? currentDriverIncidentCount
                : null;
            counterSource = IracingIncidentCounterSource.CurrentDriver;
            return true;
        }

        identity = canonicalIdentity;
        incidentCount = hasValidTeamIncidentCount && teamIncidentCount is >= 0
            ? teamIncidentCount
            : null;
        counterSource = IracingIncidentCounterSource.Team;
        return true;
    }

    public static bool TryGetReplayFocusIdentity(string identity, out string focusIdentity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var segments = identity.Split(':');
        if (segments.Length == 4 &&
            string.Equals(segments[0], CarIndexSegment, StringComparison.Ordinal) &&
            TryParsePositiveOrZero(segments[1], out var carIndex) &&
            ((string.Equals(segments[2], TeamSegment, StringComparison.Ordinal) &&
              TryParsePositive(segments[3], out _)) ||
             (string.Equals(segments[2], UserSegment, StringComparison.Ordinal) &&
              TryParsePositive(segments[3], out _))))
        {
            focusIdentity = identity;
            return true;
        }

        if (segments.Length == 6 &&
            string.Equals(segments[0], CarIndexSegment, StringComparison.Ordinal) &&
            TryParsePositiveOrZero(segments[1], out carIndex) &&
            string.Equals(segments[2], TeamSegment, StringComparison.Ordinal) &&
            TryParsePositive(segments[3], out var teamId) &&
            string.Equals(segments[4], DriverUserSegment, StringComparison.Ordinal) &&
            TryParsePositive(segments[5], out _))
        {
            focusIdentity = CreateTeamScoped(carIndex, teamId);
            return true;
        }

        focusIdentity = string.Empty;
        return false;
    }

    private static bool TryParsePositive(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
        parsed > 0;

    private static bool TryParsePositiveOrZero(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
        parsed >= 0;
}
