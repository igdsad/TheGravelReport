using System.Text;

namespace IncidentReview.Domain;

internal static class DomainText
{
    public static bool TryNormalizeRequired(
        string? input,
        int maximumLength,
        bool allowMultiline,
        out string normalized)
    {
        if (TryNormalizeOptional(input, maximumLength, allowMultiline, out var candidate) &&
            candidate is not null)
        {
            normalized = candidate;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static bool TryNormalizeOptional(
        string? input,
        int maximumLength,
        bool allowMultiline,
        out string? normalized)
    {
        normalized = null;

        if (input is null)
        {
            return true;
        }

        var candidate = allowMultiline
            ? input.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : input;
        candidate = candidate.Trim();

        if (candidate.Length == 0)
        {
            return true;
        }

        if (!IsWellFormedWithoutDisallowedControls(candidate, allowMultiline))
        {
            return false;
        }

        candidate = candidate.Normalize(NormalizationForm.FormC);
        if (candidate.Length > maximumLength)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    public static bool IsWellFormedWithoutDisallowedControls(
        string value,
        bool allowMultiline)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                {
                    return false;
                }

                continue;
            }

            if (char.IsLowSurrogate(character) ||
                (char.IsControl(character) &&
                 !(allowMultiline && character is '\n' or '\t')))
            {
                return false;
            }
        }

        return true;
    }
}
