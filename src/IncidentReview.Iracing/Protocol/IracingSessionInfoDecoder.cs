using System.Globalization;

namespace IncidentReview.Iracing.Protocol;

internal static class IracingSessionInfoDecoder
{
    public static bool TryGetReplayMetadata(
        string sessionInfo,
        out IracingReplayMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(sessionInfo);
        metadata = null;

        var context = ReadReplayContextMetadata(sessionInfo);
        if (context.Player is not { } player || context.CameraGroups.Count == 0)
        {
            return false;
        }

        metadata = new IracingReplayMetadata(
            player.CarIndex,
            player.CarNumberRaw,
            context.CameraGroups);
        return true;
    }

    public static IracingReplayContextMetadata ReadReplayContextMetadata(string sessionInfo)
    {
        ArgumentNullException.ThrowIfNull(sessionInfo);
        IracingPlayerMetadata? player = null;
        if (TryTokenizeSection(sessionInfo, "DriverInfo", out var driverLines) &&
            TryReadPlayer(
                driverLines,
                out var playerCarIndex,
                out var playerCarNumberRaw,
                out var driverDisplayName))
        {
            player = new IracingPlayerMetadata(
                playerCarIndex,
                playerCarNumberRaw,
                driverDisplayName);
        }

        IReadOnlyList<IracingCameraGroupMetadata> cameraGroups = [];
        if (TryTokenizeSection(sessionInfo, "CameraInfo", out var cameraLines))
        {
            _ = TryReadCameraGroups(cameraLines, out cameraGroups);
        }

        return new IracingReplayContextMetadata(player, cameraGroups);
    }

    private static bool TryTokenizeSection(
        string sessionInfo,
        string sectionName,
        out IReadOnlyList<YamlLine> section)
    {
        var rawLines = sessionInfo.Split('\n');
        var sectionStart = -1;
        var sectionEnd = rawLines.Length;
        for (var index = 0; index < rawLines.Length; index++)
        {
            var rawLine = rawLines[index];
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            var content = StripComment(line.AsSpan()).TrimEnd();
            if (content.IsEmpty || content.SequenceEqual("---") || content.SequenceEqual("..."))
            {
                continue;
            }

            var isTopLevel = line.Length > 0 && line[0] is not (' ' or '\t');
            if (!isTopLevel)
            {
                continue;
            }

            if (TrySplitMapping(content, out var key, out var scalar) &&
                string.Equals(key, sectionName, StringComparison.Ordinal) &&
                scalar.IsEmpty)
            {
                if (sectionStart >= 0)
                {
                    section = [];
                    return false;
                }

                sectionStart = index;
                continue;
            }

            if (sectionStart >= 0 && sectionEnd == rawLines.Length)
            {
                sectionEnd = index;
            }
        }

        if (sectionStart < 0)
        {
            section = [];
            return false;
        }

        var sectionText = string.Join(
            '\n',
            rawLines.Skip(sectionStart).Take(sectionEnd - sectionStart));
        if (!TryTokenize(sectionText, out var lines))
        {
            section = [];
            return false;
        }

        return TryGetSection(lines, sectionName, out section);
    }

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

    private static bool TryReadPlayer(
        IReadOnlyList<YamlLine> lines,
        out int playerCarIndex,
        out int playerCarNumberRaw,
        out string? driverDisplayName)
    {
        playerCarIndex = default;
        playerCarNumberRaw = default;
        driverDisplayName = null;
        if (!TryGetDirectIndent(lines, out var directIndent) ||
            !TryGetUniqueInteger(lines, directIndent, "DriverCarIdx", out playerCarIndex) ||
            playerCarIndex < 0 ||
            !TryGetUniqueEmptyMapping(lines, directIndent, "Drivers", out var driversIndex))
        {
            return false;
        }

        var matchingNumber = default(int?);
        string? matchingDisplayName = null;
        for (var index = driversIndex + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.IsSequence ||
                !string.Equals(line.Key, "CarIdx", StringComparison.Ordinal) ||
                line.Indent < directIndent ||
                !TryParseNonNegativeInt(line.Scalar, out var carIndex))
            {
                continue;
            }

            var entryEnd = FindNextSequenceAtOrAbove(lines, index + 1, line.Indent);
            if (carIndex != playerCarIndex)
            {
                index = entryEnd - 1;
                continue;
            }

            if (matchingNumber is not null ||
                !TryGetUniqueInteger(
                    lines,
                    index + 1,
                    entryEnd,
                    line.Indent + 1,
                    "CarNumberRaw",
                    out var number) ||
                number < 0)
            {
                return false;
            }

            matchingNumber = number;
            matchingDisplayName = TryGetUniqueScalar(
                lines,
                index + 1,
                entryEnd,
                line.Indent + 1,
                "UserName",
                out var userName)
                ? userName
                : null;
            index = entryEnd - 1;
        }

        if (matchingNumber is not { } rawNumber)
        {
            return false;
        }

        playerCarNumberRaw = rawNumber;
        driverDisplayName = matchingDisplayName;
        return true;
    }

    private static bool TryReadCameraGroups(
        IReadOnlyList<YamlLine> lines,
        out IReadOnlyList<IracingCameraGroupMetadata> groups)
    {
        groups = [];
        if (!TryGetDirectIndent(lines, out var directIndent) ||
            !TryGetUniqueEmptyMapping(lines, directIndent, "Groups", out var groupsIndex))
        {
            return false;
        }

        var result = new List<IracingCameraGroupMetadata>();
        var seenNumbers = new HashSet<int>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = groupsIndex + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.IsSequence ||
                !string.Equals(line.Key, "GroupNum", StringComparison.Ordinal) ||
                line.Indent < directIndent ||
                !TryParseNonNegativeInt(line.Scalar, out var groupNumber))
            {
                continue;
            }

            var groupEnd = FindNextSequenceWithKeyAtOrAbove(
                lines,
                index + 1,
                line.Indent,
                "GroupNum");
            if (!TryGetUniqueScalar(
                    lines,
                    index + 1,
                    groupEnd,
                    line.Indent + 1,
                    "GroupName",
                    out var groupName) ||
                string.IsNullOrWhiteSpace(groupName) ||
                !TryFindEmptyMapping(
                    lines,
                    index + 1,
                    groupEnd,
                    line.Indent + 1,
                    "Cameras",
                    out var camerasIndex) ||
                !TryReadCameraNumbers(
                    lines,
                    camerasIndex + 1,
                    groupEnd,
                    lines[camerasIndex].Indent,
                    out var cameraNumbers) ||
                !seenNumbers.Add(groupNumber) ||
                !seenNames.Add(groupName))
            {
                return false;
            }

            result.Add(new IracingCameraGroupMetadata(
                groupNumber,
                groupName,
                cameraNumbers));
            index = groupEnd - 1;
        }

        if (result.Count == 0)
        {
            return false;
        }

        groups = result;
        return true;
    }

    private static bool TryReadCameraNumbers(
        IReadOnlyList<YamlLine> lines,
        int start,
        int end,
        int minimumIndent,
        out IReadOnlyList<int> cameraNumbers)
    {
        var result = new List<int>();
        var seen = new HashSet<int>();
        for (var index = start; index < end; index++)
        {
            var line = lines[index];
            if (line.IsSequence &&
                line.Indent >= minimumIndent &&
                string.Equals(line.Key, "CameraNum", StringComparison.Ordinal))
            {
                if (!TryParseNonNegativeInt(line.Scalar, out var cameraNumber) ||
                    !seen.Add(cameraNumber))
                {
                    cameraNumbers = [];
                    return false;
                }

                result.Add(cameraNumber);
            }
        }

        cameraNumbers = result;
        return result.Count > 0;
    }

    private static int FindNextSequenceAtOrAbove(
        IReadOnlyList<YamlLine> lines,
        int start,
        int maximumIndent)
    {
        for (var index = start; index < lines.Count; index++)
        {
            if (lines[index].IsSequence && lines[index].Indent <= maximumIndent)
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static int FindNextSequenceWithKeyAtOrAbove(
        IReadOnlyList<YamlLine> lines,
        int start,
        int maximumIndent,
        string key)
    {
        for (var index = start; index < lines.Count; index++)
        {
            if (lines[index].IsSequence &&
                lines[index].Indent <= maximumIndent &&
                string.Equals(lines[index].Key, key, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static bool TryGetSection(
        IReadOnlyList<YamlLine> lines,
        string sectionName,
        out IReadOnlyList<YamlLine> section)
    {
        var sectionStart = -1;
        var sectionEnd = lines.Count;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Indent != 0 || line.IsSequence)
            {
                continue;
            }

            if (sectionStart >= 0)
            {
                sectionEnd = index;
                break;
            }

            if (string.Equals(line.Key, sectionName, StringComparison.Ordinal) &&
                line.Scalar.Length == 0)
            {
                sectionStart = index;
            }
        }

        if (sectionStart < 0)
        {
            section = [];
            return false;
        }

        for (var index = sectionEnd; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Indent == 0 &&
                !line.IsSequence &&
                string.Equals(line.Key, sectionName, StringComparison.Ordinal))
            {
                section = [];
                return false;
            }
        }

        section = lines.Skip(sectionStart + 1).Take(sectionEnd - sectionStart - 1).ToArray();
        return true;
    }

    private static bool TryGetDirectIndent(
        IReadOnlyList<YamlLine> lines,
        out int directIndent)
    {
        directIndent = lines
            .Where(static line => !line.IsSequence)
            .Select(static line => line.Indent)
            .DefaultIfEmpty(-1)
            .Min();
        return directIndent > 0;
    }

    private static bool TryGetUniqueInteger(
        IReadOnlyList<YamlLine> lines,
        int indent,
        string key,
        out int value) =>
        TryGetUniqueInteger(lines, 0, lines.Count, indent, key, out value);

    private static bool TryGetUniqueInteger(
        IReadOnlyList<YamlLine> lines,
        int start,
        int end,
        int minimumIndent,
        string key,
        out int value)
    {
        if (!TryGetUniqueScalar(lines, start, end, minimumIndent, key, out var scalar))
        {
            value = default;
            return false;
        }

        return TryParseNonNegativeInt(scalar, out value);
    }

    private static bool TryGetUniqueScalar(
        IReadOnlyList<YamlLine> lines,
        int start,
        int end,
        int minimumIndent,
        string key,
        out string value)
    {
        value = string.Empty;
        var found = false;
        for (var index = start; index < end; index++)
        {
            var line = lines[index];
            if (line.IsSequence ||
                line.Indent < minimumIndent ||
                !string.Equals(line.Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            if (found)
            {
                return false;
            }

            value = Unquote(line.Scalar.AsSpan()).ToString();
            found = true;
        }

        return found;
    }

    private static bool TryGetUniqueEmptyMapping(
        IReadOnlyList<YamlLine> lines,
        int indent,
        string key,
        out int index)
    {
        index = -1;
        for (var candidate = 0; candidate < lines.Count; candidate++)
        {
            var line = lines[candidate];
            if (!line.IsSequence &&
                line.Indent == indent &&
                string.Equals(line.Key, key, StringComparison.Ordinal) &&
                line.Scalar.Length == 0)
            {
                if (index >= 0)
                {
                    return false;
                }

                index = candidate;
            }
        }

        return index >= 0;
    }

    private static bool TryFindEmptyMapping(
        IReadOnlyList<YamlLine> lines,
        int start,
        int end,
        int minimumIndent,
        string key,
        out int index)
    {
        index = -1;
        for (var candidate = start; candidate < end; candidate++)
        {
            var line = lines[candidate];
            if (!line.IsSequence &&
                line.Indent >= minimumIndent &&
                string.Equals(line.Key, key, StringComparison.Ordinal) &&
                line.Scalar.Length == 0)
            {
                if (index >= 0)
                {
                    return false;
                }

                index = candidate;
            }
        }

        return index >= 0;
    }

    private static bool TryParseNonNegativeInt(string scalar, out int value) =>
        int.TryParse(
            Unquote(scalar.AsSpan()),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value) && value >= 0;

    private static bool TryTokenize(string sessionInfo, out IReadOnlyList<YamlLine> lines)
    {
        var result = new List<YamlLine>();
        foreach (var rawLine in sessionInfo.Split('\n'))
        {
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
            if (line.Contains('\r', StringComparison.Ordinal))
            {
                lines = [];
                return false;
            }

            var contentStart = 0;
            while (contentStart < line.Length && line[contentStart] == ' ')
            {
                contentStart++;
            }

            if (contentStart < line.Length && line[contentStart] == '\t')
            {
                lines = [];
                return false;
            }

            var content = StripComment(line.AsSpan(contentStart)).TrimEnd();
            if (content.IsEmpty)
            {
                continue;
            }

            if (contentStart == 0 &&
                (content.SequenceEqual("---") || content.SequenceEqual("...")))
            {
                continue;
            }

            var isSequence = content.StartsWith("- ", StringComparison.Ordinal);
            if (isSequence)
            {
                content = content[2..].TrimStart();
            }

            if (!TrySplitMapping(content, out var key, out var scalar))
            {
                lines = [];
                return false;
            }

            result.Add(new YamlLine(
                contentStart,
                isSequence,
                key,
                scalar.ToString()));
        }

        lines = result;
        return true;
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

    private sealed record YamlLine(
        int Indent,
        bool IsSequence,
        string Key,
        string Scalar);
}
