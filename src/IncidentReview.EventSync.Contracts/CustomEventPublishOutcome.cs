namespace IncidentReview.EventSync.Contracts;

/// <summary>Describes the server's authoritative result for one deterministic identity.</summary>
public enum CustomEventPublishOutcome
{
    /// <summary>The submitted event became the server's authoritative record.</summary>
    Accepted = 0,

    /// <summary>The server had already accepted that deterministic event identity.</summary>
    Duplicate = 1,
}
