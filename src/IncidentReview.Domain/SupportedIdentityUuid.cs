namespace IncidentReview.Domain;

internal static class SupportedIdentityUuid
{
    public static bool IsValid(Guid value) => Uuid5.IsValid(value) || Uuid7.IsValid(value);

    public static bool TryParseCanonical(string? text, out Guid value)
    {
        value = default;

        if (text is null ||
            !Guid.TryParseExact(text, "D", out var candidate) ||
            !string.Equals(text, candidate.ToString("D"), StringComparison.Ordinal) ||
            !IsValid(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
    }
}
