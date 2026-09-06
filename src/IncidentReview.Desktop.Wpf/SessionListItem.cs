using System.Globalization;
using IncidentReview.Application.Contracts;
using IncidentReview.Domain;

namespace IncidentReview.Desktop.Wpf;

/// <summary>Formats one immutable application session for WPF binding.</summary>
public sealed class SessionListItem
{
    internal SessionListItem(SessionSummary summary, bool isCurrent)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        IsCurrent = isCurrent;
    }

    internal SessionSummary Summary { get; }

    public SessionIdentity Id => Summary.Id;

    public bool IsCurrent { get; }

    public string DisplayName => string.Create(
        CultureInfo.CurrentCulture,
        $"{(IsCurrent ? "Current · " : string.Empty)}{Summary.Descriptor.Simulator} · session {Summary.Descriptor.SessionNumber} · {Summary.StartedAt.Value.LocalDateTime:g}");

    public string IncidentCountText => Summary.IncidentCount == 1
        ? "1 incident"
        : string.Create(CultureInfo.CurrentCulture, $"{Summary.IncidentCount} incidents");

    public int PendingIncidentCount => Summary.PendingIncidentCount;
}
