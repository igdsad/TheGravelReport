namespace IncidentReview.Results;

/// <summary>
/// A stable, machine-readable identifier for an expected failure.
/// </summary>
/// <remarks>
/// Codes contain two or more dot-separated segments. Each segment starts with
/// a lowercase ASCII letter and may then contain lowercase ASCII letters,
/// digits, or single hyphens between alphanumeric characters. Codes are at
/// most 128 characters long. For example, <c>store.sqlite.busy</c> is valid.
/// </remarks>
public readonly record struct ErrorCode
{
    private const int MaximumLength = 128;
    private readonly string? _value;

    private ErrorCode(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the machine-readable code.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The instance is the undefined <see langword="default"/> value.
    /// </exception>
    public string Value => _value
        ?? throw new InvalidOperationException("A default ErrorCode has no value.");

    internal bool IsDefined => _value is not null;

    /// <summary>
    /// Defines a validated error code.
    /// </summary>
    /// <param name="namespacedCode">The stable, lowercase namespaced code.</param>
    /// <returns>The validated code.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="namespacedCode"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="namespacedCode"/> does not follow the documented syntax.
    /// </exception>
    public static ErrorCode Define(string namespacedCode)
    {
        ArgumentNullException.ThrowIfNull(namespacedCode);

        if (!HasValidSyntax(namespacedCode))
        {
            throw new ArgumentException(
                "An error code must contain 2 or more lowercase dot-separated " +
                "segments, with each segment beginning with a letter and using " +
                "only letters, digits, or single internal hyphens.",
                nameof(namespacedCode));
        }

        return new ErrorCode(namespacedCode);
    }

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;

    private static bool HasValidSyntax(string value)
    {
        if (value.Length is 0 or > MaximumLength)
        {
            return false;
        }

        var segmentCount = 1;
        var isSegmentStart = true;
        var previousWasHyphen = false;

        foreach (var character in value)
        {
            if (character is >= 'a' and <= 'z')
            {
                isSegmentStart = false;
                previousWasHyphen = false;
                continue;
            }

            if (character is >= '0' and <= '9')
            {
                if (isSegmentStart)
                {
                    return false;
                }

                previousWasHyphen = false;
                continue;
            }

            if (character == '-')
            {
                if (isSegmentStart || previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
                continue;
            }

            if (character == '.')
            {
                if (isSegmentStart || previousWasHyphen)
                {
                    return false;
                }

                segmentCount++;
                isSegmentStart = true;
                continue;
            }

            return false;
        }

        return segmentCount >= 2 && !isSegmentStart && !previousWasHyphen;
    }
}
