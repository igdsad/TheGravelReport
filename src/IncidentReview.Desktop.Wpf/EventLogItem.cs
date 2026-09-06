using System.Globalization;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Formats one presentation event for the optional power-user log.</summary>
public sealed class EventLogItem
{
    internal EventLogItem(DateTimeOffset occurredAt, string message, bool isError)
    {
        OccurredAt = occurredAt;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        IsError = isError;
    }

    public DateTimeOffset OccurredAt { get; }

    public string Timestamp => OccurredAt.LocalDateTime.ToString("G", CultureInfo.CurrentCulture);

    public string Message { get; }

    public bool IsError { get; }
}
