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

    public string ObservedAtText => ObservedAt.ToLocalTime().ToString(
        "yyyy-MM-dd HH:mm:ss.fff zzz",
        CultureInfo.InvariantCulture);

    public string IncidentIdText
    {
        get
        {
            var value = FullIncidentIdText;
            const int visibleSuffixLength = 8;
            return value.Length <= visibleSuffixLength
                ? value
                : string.Concat("…", value.AsSpan(value.Length - visibleSuffixLength));
        }
    }

    public string FullIncidentIdText => Id.ToString();

    public string ReplayTime => FormatDuration(Incident.Position.SessionTime.Value);

    public string Points => string.Create(
        CultureInfo.CurrentCulture,
        $"+{Incident.Points.Delta} ({Incident.Points.Total} total)");

    public string Lap => Incident.Lap is null
        ? "—"
        : Incident.Lap.Value.ToString(CultureInfo.CurrentCulture);

    public string DriverText => Incident.Participant.DriverName ??
        Incident.Participant.TeamName ??
        (Incident.Participant.CarNumber is { } carNumber
            ? string.Concat("Car #", carNumber)
            : "Unknown driver");

    public string Notes => Incident.Annotation.Notes ?? string.Empty;

    public DateTimeOffset ObservedAt => Incident.ObservedAt.Value;

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.CurrentCulture)
        : value.ToString(@"m\:ss\.fff", CultureInfo.CurrentCulture);
}
