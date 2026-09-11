using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.Application.Contracts;

/// <summary>Identifies immutable application state the UI should refresh.</summary>
public abstract class ReviewUpdate
{
    private protected ReviewUpdate()
    {
    }

    public sealed class StatusChanged : ReviewUpdate
    {
        private StatusChanged(ReviewServiceStatus status, Error? error)
        {
            Status = status;
            Error = error;
        }

        public ReviewServiceStatus Status { get; }
        public Error? Error { get; }

        public static StatusChanged Create(ReviewServiceStatus status, Error? error = null)
        {
            if (!Enum.IsDefined(status) || (status == ReviewServiceStatus.Unavailable) != (error is not null))
            {
                throw new ArgumentException("Status and error do not form a valid update.", nameof(status));
            }

            return new StatusChanged(status, error);
        }
    }

    public sealed class SessionChanged : ReviewUpdate
    {
        public SessionChanged(SessionIdentity session)
        {
            ArgumentNullException.ThrowIfNull(session);
            Session = session;
        }

        public SessionIdentity Session { get; }
    }

    public sealed class IncidentChanged : ReviewUpdate
    {
        public IncidentChanged(SessionIdentity session, IncidentId incident)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(incident);
            Session = session;
            Incident = incident;
        }

        public SessionIdentity Session { get; }
        public IncidentId Incident { get; }
    }

    public sealed class CustomEventChanged : ReviewUpdate
    {
        public CustomEventChanged(SessionIdentity session, CustomEventId customEvent)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(customEvent);
            Session = session;
            CustomEvent = customEvent;
        }

        public SessionIdentity Session { get; }
        public CustomEventId CustomEvent { get; }
    }

    public sealed class SynchronizationWarning : ReviewUpdate
    {
        public SynchronizationWarning(Error error)
        {
            ArgumentNullException.ThrowIfNull(error);
            Error = error;
        }

        public Error Error { get; }
    }

    /// <summary>Indicates that durable user preferences changed.</summary>
    public sealed class PreferencesChanged : ReviewUpdate
    {
        /// <summary>Initializes a preference-change notification.</summary>
        public PreferencesChanged()
        {
        }
    }
}
