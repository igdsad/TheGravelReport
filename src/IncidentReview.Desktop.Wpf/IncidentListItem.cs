using System.Globalization;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Formats one immutable application incident for WPF binding.</summary>
public sealed class IncidentListItem
{
    internal IncidentListItem(ReviewIncident incident)
    {
        Incident = incident ?? throw new ArgumentNullException(nameof(incident));
    }

    internal ReviewIncident Incident { get; }

    public IncidentId Id => Incident.Id;

    public string ReplayTime => FormatDuration(Incident.Position.SessionTime.Value);

    public string Points => string.Create(
        CultureInfo.CurrentCulture,
        $"+{Incident.Points.Delta} ({Incident.Points.Total} total)");

    public string Lap => Incident.Lap is null
        ? "—"
        : Incident.Lap.Value.ToString(CultureInfo.CurrentCulture);

    public string Status => Incident.ReviewStatus.ToString();

    public string Notes => Incident.Annotation.Notes ?? string.Empty;

    public DateTimeOffset ObservedAt => Incident.ObservedAt.Value;

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.CurrentCulture)
        : value.ToString(@"m\:ss\.fff", CultureInfo.CurrentCulture);
}
