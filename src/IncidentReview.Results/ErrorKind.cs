namespace IncidentReview.Results;

/// <summary>
/// Describes the broad recovery category for an expected failure.
/// </summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unavailable,
    Persistence,
    Integration,
    Indeterminate,
}
