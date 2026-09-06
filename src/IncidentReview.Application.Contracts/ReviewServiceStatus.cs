namespace IncidentReview.Application.Contracts;

/// <summary>Describes the UI-visible telemetry/runtime state.</summary>
public enum ReviewServiceStatus
{
    Stopped = 0,
    WaitingForSimulator = 1,
    Connected = 2,
    Unavailable = 3,
}
