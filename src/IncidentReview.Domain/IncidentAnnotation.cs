using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Contains user-controlled notes and an optional incident category.</summary>
public sealed record IncidentAnnotation
{
    /// <summary>Gets the maximum normalized notes length in UTF-16 code units.</summary>
    public const int MaximumNotesLength = 2_000;

    private static readonly Error InvalidNotesError = DomainValidationError.Create(
        "domain.incident-annotation.invalid-notes",
        "Incident notes contain invalid text or exceed the supported length.");

    private IncidentAnnotation(string? notes, IncidentClassification? classification)
    {
        Notes = notes;
        Classification = classification;
    }

    /// <summary>Gets normalized notes, or <see langword="null"/> when empty.</summary>
    public string? Notes { get; }

    /// <summary>Gets the optional user-assigned category.</summary>
    public IncidentClassification? Classification { get; }

    /// <summary>Validates and normalizes annotation input.</summary>
    public static Result<IncidentAnnotation> TryCreate(
        string? notes,
        IncidentClassification? classification)
    {
        if (!DomainText.TryNormalizeOptional(
                notes,
                MaximumNotesLength,
                allowMultiline: true,
                out var normalizedNotes))
        {
            return Result<IncidentAnnotation>.Failure(InvalidNotesError);
        }

        return Result<IncidentAnnotation>.Success(
            new IncidentAnnotation(normalizedNotes, classification));
    }
}
