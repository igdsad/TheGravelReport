using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

/// <summary>Represents the complete local synchronization state of a custom event.</summary>
public abstract record CustomEventSynchronization
{
    private CustomEventSynchronization()
    {
    }

    /// <summary>Represents an event that remains in the durable local outbox.</summary>
    public sealed record Pending : CustomEventSynchronization
    {
        private Pending()
        {
        }

        /// <summary>Gets the single immutable pending value.</summary>
        public static Pending Instance { get; } = new();
    }

    /// <summary>Represents an event accepted or deduplicated by the remote endpoint.</summary>
    public sealed record Synchronized : CustomEventSynchronization
    {
        private Synchronized(UtcInstant synchronizedAt)
        {
            SynchronizedAt = synchronizedAt;
        }

        /// <summary>Gets when remote synchronization was confirmed.</summary>
        public UtcInstant SynchronizedAt { get; }

        /// <summary>Creates a confirmed synchronization state.</summary>
        public static Synchronized Create(UtcInstant synchronizedAt)
        {
            ArgumentNullException.ThrowIfNull(synchronizedAt);
            return new Synchronized(synchronizedAt);
        }
    }
}
