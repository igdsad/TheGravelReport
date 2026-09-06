namespace IncidentReview.Domain;

internal static class Uuid7
{
    public static bool IsValid(Guid value) =>
        value != Guid.Empty &&
        value.Version == 7 &&
        value.Variant is >= 8 and <= 11;

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
