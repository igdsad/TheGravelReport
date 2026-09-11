using System.Globalization;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Formats one immutable user-created event for WPF binding.</summary>
public sealed class CustomEventListItem
{
    internal CustomEventListItem(ReviewCustomEvent customEvent)
    {
        CustomEvent = customEvent ?? throw new ArgumentNullException(nameof(customEvent));
    }

    internal ReviewCustomEvent CustomEvent { get; }

    public CustomEventId Id => CustomEvent.Id;

    public string OccurredAtText =>
        CustomEvent.OccurredAt.Value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

    public string CustomEventIdText
    {
        get
        {
            var value = FullCustomEventIdText;
            const int visibleSuffixLength = 8;
            return value.Length <= visibleSuffixLength
                ? value
                : string.Concat("…", value.AsSpan(value.Length - visibleSuffixLength));
        }
    }

    public string FullCustomEventIdText => Id.ToString();

    public string ReplayTime => FormatDuration(CustomEvent.Position.SessionTime.Value);

    public string Submitter => CustomEvent.Submitter.Value;

    public string SynchronizationStatus =>
        CustomEvent.IsSynchronized ? "Synced" : "Saved locally";

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.CurrentCulture)
        : value.ToString(@"m\:ss\.fff", CultureInfo.CurrentCulture);
}
