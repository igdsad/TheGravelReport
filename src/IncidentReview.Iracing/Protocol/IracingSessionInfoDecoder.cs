using System.Globalization;

namespace IncidentReview.Iracing.Protocol;

internal static class IracingSessionInfoDecoder
{
    public static long? TryGetSubSessionId(string sessionInfo)
    {
        ArgumentNullException.ThrowIfNull(sessionInfo);

        if (!TryGetUniqueWeekendScalar(sessionInfo, "SubSessionID", out var scalar) ||
            scalar is null ||
            !long.TryParse(
                scalar,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed <= 0)
        {
            return null;
        }

        return parsed;
    }

    public static bool UsesUtf8Encoding(string sessionInfo)
    {
        ArgumentNullException.ThrowIfNull(sessionInfo);
        return TryGetUniqueWeekendScalar(sessionInfo, "Encoding", out var scalar) &&
            string.Equals(scalar, "UTF8", StringComparison.Ordinal);
    }

    private static bool TryGetUniqueWeekendScalar(
        string sessionInfo,
        string targetKey,
        out string? value)
    {
        value = null;

        var inWeekendInfo = false;
        int? weekendIndent = null;
        int? weekendChildIndent = null;

        foreach (var rawLine in sessionInfo.Split('\n'))
        {
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            if (line.Contains('\r', StringComparison.Ordinal))
            {
                return false;
            }

            var contentStart = 0;
            while (contentStart < line.Length && line[contentStart] == ' ')
            {
                contentStart++;
            }

            if (contentStart < line.Length && line[contentStart] == '\t')
            {
                return false;
            }

            var content = StripComment(line.AsSpan(contentStart)).Trim();
            if (content.IsEmpty)
            {
                continue;
            }

            if (TrySplitMapping(content, out var key, out var scalar) &&
                contentStart == 0 &&
                string.Equals(key, "WeekendInfo", StringComparison.Ordinal) &&
                scalar.IsEmpty)
            {
                inWeekendInfo = true;
                weekendIndent = contentStart;
                weekendChildIndent = null;
                continue;
            }

            if (!inWeekendInfo || weekendIndent is null)
            {
                continue;
            }

            if (contentStart <= weekendIndent)
            {
                inWeekendInfo = false;
                weekendIndent = null;
                weekendChildIndent = null;
                continue;
            }

            weekendChildIndent ??= contentStart;
            if (contentStart != weekendChildIndent ||
                !TrySplitMapping(content, out key, out scalar))
            {
                continue;
            }

            if (!string.Equals(key, targetKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (value is not null)
            {
                return false;
            }

            value = Unquote(scalar).ToString();
        }

        return true;
    }

    private static ReadOnlySpan<char> StripComment(ReadOnlySpan<char> input)
    {
        var singleQuoted = false;
        var doubleQuoted = false;
        var escaped = false;
        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (doubleQuoted && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (!doubleQuoted && character == '\'')
            {
                singleQuoted = !singleQuoted;
                continue;
            }

            if (!singleQuoted && character == '"')
            {
                doubleQuoted = !doubleQuoted;
                continue;
            }

            if (!singleQuoted && !doubleQuoted && character == '#')
            {
                return input[..index];
            }
        }

        return input;
    }

    private static bool TrySplitMapping(
        ReadOnlySpan<char> input,
        out string key,
        out ReadOnlySpan<char> scalar)
    {
        var singleQuoted = false;
        var doubleQuoted = false;
        var escaped = false;
        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (doubleQuoted && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (!doubleQuoted && character == '\'')
            {
                singleQuoted = !singleQuoted;
                continue;
            }

            if (!singleQuoted && character == '"')
            {
                doubleQuoted = !doubleQuoted;
                continue;
            }

            if (!singleQuoted && !doubleQuoted && character == ':')
            {
                key = input[..index].Trim().ToString();
                scalar = input[(index + 1)..].Trim();
                return key.Length > 0;
            }
        }

        key = string.Empty;
        scalar = default;
        return false;
    }

    private static ReadOnlySpan<char> Unquote(ReadOnlySpan<char> scalar)
    {
        if (scalar.Length >= 2 &&
            ((scalar[0] == '\'' && scalar[^1] == '\'') ||
             (scalar[0] == '"' && scalar[^1] == '"')))
        {
            return scalar[1..^1];
        }

        return scalar;
    }
}
