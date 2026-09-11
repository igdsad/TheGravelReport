namespace IncidentReview.Domain;

internal static class Uuid7
{
    public static bool IsValid(Guid value) =>
        value != Guid.Empty &&
        value.Version == 7 &&
        value.Variant is >= 8 and <= 11;
}
