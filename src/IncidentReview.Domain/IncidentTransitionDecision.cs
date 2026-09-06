namespace IncidentReview.Domain;

/// <summary>
/// Represents one member of the closed incident-counter transition decision set.
/// </summary>
public abstract class IncidentTransitionDecision
{
    private IncidentTransitionDecision()
    {
    }

    /// <summary>Represents an observation that requires no durable write.</summary>
    public sealed class NoChange : IncidentTransitionDecision
    {
        private NoChange()
        {
        }

        /// <summary>Gets the sole no-change decision.</summary>
        public static NoChange Instance { get; } = new();
    }

    /// <summary>Represents a new durable baseline.</summary>
    public sealed class EstablishBaseline :
        IncidentTransitionDecision,
        IEquatable<EstablishBaseline>
    {
        private EstablishBaseline(
            IncidentBaselineReason reason,
            IncidentCheckpoint nextCheckpoint)
        {
            Reason = reason;
            NextCheckpoint = nextCheckpoint;
        }

        /// <summary>Gets why a new baseline is required.</summary>
        public IncidentBaselineReason Reason { get; }

        /// <summary>Gets the checkpoint to establish atomically.</summary>
        public IncidentCheckpoint NextCheckpoint { get; }

        internal static EstablishBaseline Create(
            IncidentBaselineReason reason,
            IncidentCheckpoint nextCheckpoint)
        {
            ArgumentNullException.ThrowIfNull(reason);
            ArgumentNullException.ThrowIfNull(nextCheckpoint);
            return new EstablishBaseline(reason, nextCheckpoint);
        }

        /// <inheritdoc />
        public bool Equals(EstablishBaseline? other) =>
            other is not null &&
            Reason == other.Reason &&
            NextCheckpoint == other.NextCheckpoint;

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as EstablishBaseline);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Reason, NextCheckpoint);
    }

    /// <summary>Represents one cumulative counter increase and its atomic checkpoint move.</summary>
    public sealed class RecordIncrease :
        IncidentTransitionDecision,
        IEquatable<RecordIncrease>
    {
        private RecordIncrease(
            IncidentPoints points,
            IncidentCheckpoint expectedCheckpoint,
            IncidentCheckpoint nextCheckpoint,
            IncidentObservation observation)
        {
            Points = points;
            ExpectedCheckpoint = expectedCheckpoint;
            NextCheckpoint = nextCheckpoint;
            Observation = observation;
        }

        /// <summary>Gets the cumulative total and complete positive increase.</summary>
        public IncidentPoints Points { get; }

        /// <summary>Gets the durable state that the atomic write must compare.</summary>
        public IncidentCheckpoint ExpectedCheckpoint { get; }

        /// <summary>Gets the durable state that the atomic write must establish.</summary>
        public IncidentCheckpoint NextCheckpoint { get; }

        /// <summary>Gets the observation context recorded for the incident marker.</summary>
        public IncidentObservation Observation { get; }

        internal static RecordIncrease Create(
            IncidentPoints points,
            IncidentCheckpoint expectedCheckpoint,
            IncidentCheckpoint nextCheckpoint,
            IncidentObservation observation)
        {
            ArgumentNullException.ThrowIfNull(points);
            ArgumentNullException.ThrowIfNull(expectedCheckpoint);
            ArgumentNullException.ThrowIfNull(nextCheckpoint);
            ArgumentNullException.ThrowIfNull(observation);

            return new RecordIncrease(
                points,
                expectedCheckpoint,
                nextCheckpoint,
                observation);
        }

        /// <inheritdoc />
        public bool Equals(RecordIncrease? other) =>
            other is not null &&
            Points == other.Points &&
            ExpectedCheckpoint == other.ExpectedCheckpoint &&
            NextCheckpoint == other.NextCheckpoint &&
            Observation == other.Observation;

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as RecordIncrease);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(
            Points,
            ExpectedCheckpoint,
            NextCheckpoint,
            Observation);
    }
}
