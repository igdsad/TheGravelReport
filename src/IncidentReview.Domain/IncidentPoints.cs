using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Captures a cumulative incident-point total and its detected increase.</summary>
public sealed record IncidentPoints
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.incident-points.invalid",
        "Incident points require a non-negative total and a positive delta no greater than that total.");

    private IncidentPoints(int total, int delta)
    {
        Total = total;
        Delta = delta;
    }

    /// <summary>Gets the cumulative incident-point total after the increase.</summary>
    public int Total { get; }

    /// <summary>Gets the positive increase represented by this incident.</summary>
    public int Delta { get; }

    /// <summary>Validates point values read from an untrusted boundary.</summary>
    public static Result<IncidentPoints> TryCreate(int total, int delta) =>
        total >= 0 && delta > 0 && delta <= total
            ? Result<IncidentPoints>.Success(new IncidentPoints(total, delta))
            : Result<IncidentPoints>.Failure(InvalidError);
}
