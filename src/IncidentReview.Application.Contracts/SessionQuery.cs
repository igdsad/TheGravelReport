namespace IncidentReview.Application.Contracts;

/// <summary>Requests all durable review sessions in newest-first order.</summary>
public sealed class SessionQuery
{
    private SessionQuery()
    {
    }

    public static SessionQuery All { get; } = new();
}
