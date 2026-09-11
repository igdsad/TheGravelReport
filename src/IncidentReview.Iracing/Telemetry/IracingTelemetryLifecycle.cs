using System.Collections.ObjectModel;
using IncidentReview.Domain;
using IncidentReview.Iracing.Protocol;
using IncidentReview.Results;
using IncidentReview.Telemetry.Contracts;

namespace IncidentReview.Iracing.Telemetry;

internal sealed record IracingConnectionIdentity
{
    private IracingConnectionIdentity(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static IracingConnectionIdentity Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new IracingConnectionIdentity(value);
    }
}

internal sealed record IracingMonotonicTimestamp
{
    private readonly long _value;

    private IracingMonotonicTimestamp(long value)
    {
        _value = value;
    }

    public static IracingMonotonicTimestamp Capture(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new IracingMonotonicTimestamp(timeProvider.GetTimestamp());
    }

    public TimeSpan ElapsedUntil(
        IracingMonotonicTimestamp end,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(end);
        ArgumentNullException.ThrowIfNull(timeProvider);
        return timeProvider.GetElapsedTime(_value, end._value);
    }
}

internal sealed record IracingLogicalConnection(
    IracingConnectionIdentity Identity,
    IracingMonotonicTimestamp LastSuccessfullyDecodedSampleTimestamp);

internal sealed record IracingTelemetryTransitionProjection(
    SimulatorSessionDescriptor Session,
    string IncidentCounterSet,
    OnTrackState OnTrackState)
{
    public static IracingTelemetryTransitionProjection From(TelemetrySample sample) => new(
        sample.Session,
        string.Concat(sample.IncidentCounters
            .OrderBy(
                static counter => counter.Participant.Identity.Value,
                StringComparer.Ordinal)
            .Select(static counter =>
                $"{counter.Participant.Identity.Value.Length}:" +
                $"{counter.Participant.Identity.Value}:" +
                $"{counter.IncidentCounter.Value};")),
        sample.OnTrackState);
}

internal enum IracingReaderOwnership
{
    Closed,
    Open,
}

internal abstract record IracingTelemetryLifecycleState
{
    private IracingTelemetryLifecycleState()
    {
    }

    public abstract IracingReaderOwnership ReaderOwnership { get; }

    public abstract IracingConnectionIdentity? ConnectionIdentity { get; }

    public abstract IracingLogicalConnection? LogicalConnection { get; }

    internal sealed record AwaitingEndpoint : IracingTelemetryLifecycleState
    {
        public static AwaitingEndpoint Instance { get; } = new();

        private AwaitingEndpoint()
        {
        }

        public override IracingReaderOwnership ReaderOwnership =>
            IracingReaderOwnership.Closed;

        public override IracingConnectionIdentity? ConnectionIdentity => null;

        public override IracingLogicalConnection? LogicalConnection => null;
    }

    internal sealed record AwaitingEndpointAfterDiagnostic : IracingTelemetryLifecycleState
    {
        public static AwaitingEndpointAfterDiagnostic Instance { get; } = new();

        private AwaitingEndpointAfterDiagnostic()
        {
        }

        public override IracingReaderOwnership ReaderOwnership =>
            IracingReaderOwnership.Closed;

        public override IracingConnectionIdentity? ConnectionIdentity => null;

        public override IracingLogicalConnection? LogicalConnection => null;
    }

    internal sealed record Priming(IracingConnectionIdentity Identity) :
        IracingTelemetryLifecycleState
    {
        public override IracingReaderOwnership ReaderOwnership =>
            IracingReaderOwnership.Open;

        public override IracingConnectionIdentity ConnectionIdentity => Identity;

        public override IracingLogicalConnection? LogicalConnection => null;
    }

    internal sealed record PrimingDegraded(IracingConnectionIdentity Identity) :
        IracingTelemetryLifecycleState
    {
        public override IracingReaderOwnership ReaderOwnership =>
            IracingReaderOwnership.Open;

        public override IracingConnectionIdentity ConnectionIdentity => Identity;

        public override IracingLogicalConnection? LogicalConnection => null;
    }

    internal sealed record ReopeningBeforeFirstSample(IracingConnectionIdentity Identity) :
        IracingTelemetryLifecycleState
    {
        public override IracingReaderOwnership ReaderOwnership =>
            IracingReaderOwnership.Closed;

        public override IracingConnectionIdentity ConnectionIdentity => Identity;

        public override IracingLogicalConnection? LogicalConnection => null;
    }

    internal sealed record Active(
        IracingLogicalConnection Connection,
        IracingTelemetryTransitionProjection LastPublishedTransition) :
        IracingTelemetryLifecycleState
    {
        public override IracingReaderOwnership ReaderOwnership =>
            IracingReaderOwnership.Open;

        public override IracingConnectionIdentity ConnectionIdentity => Connection.Identity;

        public override IracingLogicalConnection LogicalConnection => Connection;
    }

    internal sealed record Degraded(IracingLogicalConnection Connection) :
        IracingTelemetryLifecycleState
    {
        public override IracingReaderOwnership ReaderOwnership =>
            IracingReaderOwnership.Open;

        public override IracingConnectionIdentity ConnectionIdentity => Connection.Identity;

        public override IracingLogicalConnection LogicalConnection => Connection;
    }

    internal sealed record Reopening(IracingLogicalConnection Connection) :
        IracingTelemetryLifecycleState
    {
        public override IracingReaderOwnership ReaderOwnership =>
            IracingReaderOwnership.Closed;

        public override IracingConnectionIdentity ConnectionIdentity => Connection.Identity;

        public override IracingLogicalConnection LogicalConnection => Connection;
    }
}

internal abstract record IracingLogicalConnectionAge
{
    private IracingLogicalConnectionAge()
    {
    }

    internal sealed record NotEstablished : IracingLogicalConnectionAge
    {
        public static NotEstablished Instance { get; } = new();

        private NotEstablished()
        {
        }
    }

    internal sealed record Measured : IracingLogicalConnectionAge
    {
        public Measured(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsed),
                    elapsed,
                    "Elapsed monotonic time cannot be negative.");
            }

            Elapsed = elapsed;
        }

        public TimeSpan Elapsed { get; }
    }
}

internal abstract record IracingTelemetryLifecycleInput
{
    private IracingTelemetryLifecycleInput()
    {
    }

    internal sealed record ReaderOpened(IracingConnectionIdentity Identity) :
        IracingTelemetryLifecycleInput;

    internal sealed record ReaderOpenFailed(IracingLogicalConnectionAge ConnectionAge) :
        IracingTelemetryLifecycleInput;

    internal sealed record SampleAccepted(
        IracingFrameSnapshot Frame,
        TelemetrySample Sample,
        IracingMonotonicTimestamp Timestamp,
        IracingLogicalConnectionAge PreviousSampleAge) :
        IracingTelemetryLifecycleInput;

    internal sealed record SampleRejected(
        Error Error,
        IracingLogicalConnectionAge PreviousSampleAge) :
        IracingTelemetryLifecycleInput;

    internal sealed record NoData(IracingLogicalConnectionAge ConnectionAge) :
        IracingTelemetryLifecycleInput;

    internal sealed record InvalidFrame(IracingLogicalConnectionAge ConnectionAge) :
        IracingTelemetryLifecycleInput;

    internal sealed record SdkDisconnected : IracingTelemetryLifecycleInput
    {
        public static SdkDisconnected Instance { get; } = new();

        private SdkDisconnected()
        {
        }
    }
}

internal abstract record IracingTelemetryLifecycleEffect
{
    private IracingTelemetryLifecycleEffect()
    {
    }

    internal sealed record CloseReader : IracingTelemetryLifecycleEffect
    {
        public static CloseReader Instance { get; } = new();

        private CloseReader()
        {
        }
    }

    internal sealed record PublishReplayUnavailable : IracingTelemetryLifecycleEffect
    {
        public static PublishReplayUnavailable Instance { get; } = new();

        private PublishReplayUnavailable()
        {
        }
    }

    internal sealed record PublishReplayFrame(
        IracingFrameSnapshot Frame,
        TelemetrySample Sample) :
        IracingTelemetryLifecycleEffect;

    internal sealed record PublishTelemetry(TelemetryEvent Event) :
        IracingTelemetryLifecycleEffect;
}

internal enum IracingTelemetryLoopDirective
{
    Continue,
    WaitForData,
    DelayBeforeReconnect,
}

internal sealed record IracingTelemetryLifecycleTransition(
    IracingTelemetryLifecycleState State,
    ReadOnlyCollection<IracingTelemetryLifecycleEffect> Effects,
    IracingTelemetryLoopDirective LoopDirective);

internal static class IracingTelemetryLifecycleReducer
{
    public static IracingTelemetryLifecycleState InitialState =>
        IracingTelemetryLifecycleState.AwaitingEndpoint.Instance;

    public static bool RequiresReaderOpen(IracingTelemetryLifecycleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.ReaderOwnership == IracingReaderOwnership.Closed;
    }

    public static bool TryGetRetainedConnectionIdentity(
        IracingTelemetryLifecycleState state,
        out IracingConnectionIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(state);
        identity = state.ReaderOwnership == IracingReaderOwnership.Closed
            ? state.ConnectionIdentity
            : null;
        return identity is not null;
    }

    public static IracingConnectionIdentity GetConnectionIdentityForRead(
        IracingTelemetryLifecycleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return (state.ReaderOwnership, state.ConnectionIdentity) switch
        {
            (IracingReaderOwnership.Open, { } identity) => identity,
            _ => throw InvalidStateForReader(state),
        };
    }

    public static bool TryGetLastSuccessfullyDecodedSampleTimestamp(
        IracingTelemetryLifecycleState state,
        out IracingMonotonicTimestamp? timestamp)
    {
        ArgumentNullException.ThrowIfNull(state);
        var connection = state.LogicalConnection;
        timestamp = connection?.LastSuccessfullyDecodedSampleTimestamp;
        return timestamp is not null;
    }

    public static IracingTelemetryLifecycleTransition Reduce(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ValidateConnectionAge(state, input);
        return input switch
        {
            IracingTelemetryLifecycleInput.ReaderOpened opened =>
                ReduceReaderOpened(state, opened),
            IracingTelemetryLifecycleInput.ReaderOpenFailed failed =>
                ReduceReaderOpenFailed(state, failed),
            IracingTelemetryLifecycleInput.SampleAccepted accepted =>
                ReduceSampleAccepted(state, accepted),
            IracingTelemetryLifecycleInput.SampleRejected rejected =>
                ReduceSampleRejected(state, rejected),
            IracingTelemetryLifecycleInput.NoData noData => ReduceNoData(state, noData),
            IracingTelemetryLifecycleInput.InvalidFrame invalid =>
                ReduceInvalidFrame(state, invalid),
            IracingTelemetryLifecycleInput.SdkDisconnected => ReduceDisconnected(state),
            _ => throw new InvalidOperationException("The telemetry lifecycle input is undefined."),
        };
    }

    private static IracingTelemetryLifecycleTransition ReduceReaderOpened(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput.ReaderOpened input) =>
        state switch
        {
            IracingTelemetryLifecycleState.AwaitingEndpoint or
            IracingTelemetryLifecycleState.AwaitingEndpointAfterDiagnostic =>
                Move(new IracingTelemetryLifecycleState.Priming(input.Identity)),
            IracingTelemetryLifecycleState.ReopeningBeforeFirstSample reopening
                when reopening.Identity == input.Identity =>
                Move(new IracingTelemetryLifecycleState.PrimingDegraded(reopening.Identity)),
            IracingTelemetryLifecycleState.Reopening reopening
                when reopening.Connection.Identity == input.Identity =>
                Move(new IracingTelemetryLifecycleState.Degraded(reopening.Connection)),
            _ => throw InvalidTransition(state, input),
        };

    private static IracingTelemetryLifecycleTransition ReduceReaderOpenFailed(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput.ReaderOpenFailed input) =>
        state switch
        {
            IracingTelemetryLifecycleState.AwaitingEndpoint => Move(
                IracingTelemetryLifecycleState.AwaitingEndpointAfterDiagnostic.Instance,
                IracingTelemetryLoopDirective.DelayBeforeReconnect,
                new IracingTelemetryLifecycleEffect.PublishTelemetry(
                    TelemetryUnavailable.Create(IracingErrors.TelemetryUnavailable))),
            IracingTelemetryLifecycleState.AwaitingEndpointAfterDiagnostic => Move(
                state,
                IracingTelemetryLoopDirective.DelayBeforeReconnect),
            IracingTelemetryLifecycleState.ReopeningBeforeFirstSample => Move(
                IracingTelemetryLifecycleState.AwaitingEndpointAfterDiagnostic.Instance,
                IracingTelemetryLoopDirective.DelayBeforeReconnect),
            IracingTelemetryLifecycleState.Reopening when IsExpired(state, input.ConnectionAge) =>
                Move(
                    IracingTelemetryLifecycleState.AwaitingEndpointAfterDiagnostic.Instance,
                    IracingTelemetryLoopDirective.DelayBeforeReconnect,
                    new IracingTelemetryLifecycleEffect.PublishTelemetry(
                        TelemetryDisconnected.Instance)),
            IracingTelemetryLifecycleState.Reopening => Move(
                state,
                IracingTelemetryLoopDirective.DelayBeforeReconnect),
            _ => throw InvalidTransition(state, input),
        };

    private static IracingTelemetryLifecycleTransition ReduceSampleAccepted(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput.SampleAccepted input)
    {
        var projection = IracingTelemetryTransitionProjection.From(input.Sample);
        return state switch
        {
            IracingTelemetryLifecycleState.Priming priming => FirstSample(
                priming.Identity,
                input,
                projection),
            IracingTelemetryLifecycleState.PrimingDegraded priming => FirstSample(
                priming.Identity,
                input,
                projection),
            IracingTelemetryLifecycleState.Active when IsExpired(
                state,
                input.PreviousSampleAge) => DisconnectOpenReader(),
            IracingTelemetryLifecycleState.Degraded when IsExpired(
                state,
                input.PreviousSampleAge) => DisconnectOpenReader(),
            IracingTelemetryLifecycleState.Active active => ActiveSample(
                active,
                input,
                projection),
            IracingTelemetryLifecycleState.Degraded degraded => Move(
                new IracingTelemetryLifecycleState.Active(
                    degraded.Connection with
                    {
                        LastSuccessfullyDecodedSampleTimestamp = input.Timestamp,
                    },
                    projection),
                IracingTelemetryLoopDirective.Continue,
                new IracingTelemetryLifecycleEffect.PublishReplayFrame(
                    input.Frame,
                    input.Sample),
                new IracingTelemetryLifecycleEffect.PublishTelemetry(
                    TelemetrySampleObserved.Create(input.Sample))),
            _ => throw InvalidTransition(state, input),
        };
    }

    private static IracingTelemetryLifecycleTransition ReduceSampleRejected(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput.SampleRejected input) =>
        state switch
        {
            IracingTelemetryLifecycleState.Priming priming => Move(
                new IracingTelemetryLifecycleState.PrimingDegraded(priming.Identity),
                IracingTelemetryLoopDirective.Continue,
                IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                new IracingTelemetryLifecycleEffect.PublishTelemetry(
                    TelemetryUnavailable.Create(input.Error))),
            IracingTelemetryLifecycleState.PrimingDegraded => Move(state),
            IracingTelemetryLifecycleState.Active when IsExpired(
                state,
                input.PreviousSampleAge) => Move(
                    IracingTelemetryLifecycleState.AwaitingEndpoint.Instance,
                    IracingTelemetryLoopDirective.DelayBeforeReconnect,
                    IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                    IracingTelemetryLifecycleEffect.CloseReader.Instance,
                    new IracingTelemetryLifecycleEffect.PublishTelemetry(
                        TelemetryUnavailable.Create(input.Error)),
                    new IracingTelemetryLifecycleEffect.PublishTelemetry(
                        TelemetryDisconnected.Instance)),
            IracingTelemetryLifecycleState.Degraded when IsExpired(
                state,
                input.PreviousSampleAge) => DisconnectOpenReader(),
            IracingTelemetryLifecycleState.Active active => Move(
                new IracingTelemetryLifecycleState.Degraded(active.Connection),
                IracingTelemetryLoopDirective.Continue,
                IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                new IracingTelemetryLifecycleEffect.PublishTelemetry(
                    TelemetryUnavailable.Create(input.Error))),
            IracingTelemetryLifecycleState.Degraded => Move(state),
            _ => throw InvalidTransition(state, input),
        };

    private static IracingTelemetryLifecycleTransition ReduceNoData(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput.NoData input) =>
        state switch
        {
            IracingTelemetryLifecycleState.Priming or
            IracingTelemetryLifecycleState.PrimingDegraded => Move(
                state,
                IracingTelemetryLoopDirective.WaitForData),
            IracingTelemetryLifecycleState.Active when IsExpired(
                state,
                input.ConnectionAge) => DisconnectOpenReader(),
            IracingTelemetryLifecycleState.Degraded when IsExpired(
                state,
                input.ConnectionAge) => DisconnectOpenReader(),
            IracingTelemetryLifecycleState.Active or
            IracingTelemetryLifecycleState.Degraded => Move(
                state,
                IracingTelemetryLoopDirective.WaitForData),
            _ => throw InvalidTransition(state, input),
        };

    private static IracingTelemetryLifecycleTransition ReduceInvalidFrame(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput.InvalidFrame input) =>
        state switch
        {
            IracingTelemetryLifecycleState.Priming priming => Move(
                new IracingTelemetryLifecycleState.ReopeningBeforeFirstSample(priming.Identity),
                IracingTelemetryLoopDirective.DelayBeforeReconnect,
                IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                IracingTelemetryLifecycleEffect.CloseReader.Instance,
                new IracingTelemetryLifecycleEffect.PublishTelemetry(
                    TelemetryUnavailable.Create(IracingErrors.InvalidTelemetryFrame))),
            IracingTelemetryLifecycleState.PrimingDegraded priming => Move(
                new IracingTelemetryLifecycleState.ReopeningBeforeFirstSample(priming.Identity),
                IracingTelemetryLoopDirective.DelayBeforeReconnect,
                IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                IracingTelemetryLifecycleEffect.CloseReader.Instance),
            IracingTelemetryLifecycleState.Active when IsExpired(
                state,
                input.ConnectionAge) => Move(
                IracingTelemetryLifecycleState.AwaitingEndpoint.Instance,
                IracingTelemetryLoopDirective.DelayBeforeReconnect,
                IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                IracingTelemetryLifecycleEffect.CloseReader.Instance,
                    new IracingTelemetryLifecycleEffect.PublishTelemetry(
                        TelemetryUnavailable.Create(IracingErrors.InvalidTelemetryFrame)),
                    new IracingTelemetryLifecycleEffect.PublishTelemetry(
                        TelemetryDisconnected.Instance)),
            IracingTelemetryLifecycleState.Degraded when IsExpired(
                state,
                input.ConnectionAge) => DisconnectOpenReader(),
            IracingTelemetryLifecycleState.Active active => Move(
                new IracingTelemetryLifecycleState.Reopening(active.Connection),
                IracingTelemetryLoopDirective.DelayBeforeReconnect,
                IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                IracingTelemetryLifecycleEffect.CloseReader.Instance,
                new IracingTelemetryLifecycleEffect.PublishTelemetry(
                    TelemetryUnavailable.Create(IracingErrors.InvalidTelemetryFrame))),
            IracingTelemetryLifecycleState.Degraded degraded => Move(
                new IracingTelemetryLifecycleState.Reopening(degraded.Connection),
                IracingTelemetryLoopDirective.DelayBeforeReconnect,
                IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                IracingTelemetryLifecycleEffect.CloseReader.Instance),
            _ => throw InvalidTransition(state, input),
        };

    private static IracingTelemetryLifecycleTransition ReduceDisconnected(
        IracingTelemetryLifecycleState state) =>
        state switch
        {
            IracingTelemetryLifecycleState.Priming or
            IracingTelemetryLifecycleState.PrimingDegraded => Move(
                IracingTelemetryLifecycleState.AwaitingEndpoint.Instance,
                IracingTelemetryLoopDirective.DelayBeforeReconnect,
                IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
                IracingTelemetryLifecycleEffect.CloseReader.Instance),
            IracingTelemetryLifecycleState.Active or
            IracingTelemetryLifecycleState.Degraded => DisconnectOpenReader(),
            _ => throw InvalidTransition(
                state,
                IracingTelemetryLifecycleInput.SdkDisconnected.Instance),
        };

    private static IracingTelemetryLifecycleTransition FirstSample(
        IracingConnectionIdentity identity,
        IracingTelemetryLifecycleInput.SampleAccepted input,
        IracingTelemetryTransitionProjection projection)
    {
        EnsureNotEstablished(input.PreviousSampleAge);
        return Move(
            new IracingTelemetryLifecycleState.Active(
                new IracingLogicalConnection(identity, input.Timestamp),
                projection),
            IracingTelemetryLoopDirective.Continue,
            new IracingTelemetryLifecycleEffect.PublishReplayFrame(
                input.Frame,
                input.Sample),
            new IracingTelemetryLifecycleEffect.PublishTelemetry(
                TelemetryConnected.Instance),
            new IracingTelemetryLifecycleEffect.PublishTelemetry(
                TelemetrySampleObserved.Create(input.Sample)));
    }

    private static IracingTelemetryLifecycleTransition ActiveSample(
        IracingTelemetryLifecycleState.Active active,
        IracingTelemetryLifecycleInput.SampleAccepted input,
        IracingTelemetryTransitionProjection projection)
    {
        var next = new IracingTelemetryLifecycleState.Active(
            active.Connection with
            {
                LastSuccessfullyDecodedSampleTimestamp = input.Timestamp,
            },
            projection);
        return projection == active.LastPublishedTransition
            ? Move(
                next,
                IracingTelemetryLoopDirective.Continue,
                new IracingTelemetryLifecycleEffect.PublishReplayFrame(
                    input.Frame,
                    input.Sample))
            : Move(
                next,
                IracingTelemetryLoopDirective.Continue,
                new IracingTelemetryLifecycleEffect.PublishReplayFrame(
                    input.Frame,
                    input.Sample),
                new IracingTelemetryLifecycleEffect.PublishTelemetry(
                    TelemetrySampleObserved.Create(input.Sample)));
    }

    private static IracingTelemetryLifecycleTransition DisconnectOpenReader() => Move(
        IracingTelemetryLifecycleState.AwaitingEndpoint.Instance,
        IracingTelemetryLoopDirective.DelayBeforeReconnect,
        IracingTelemetryLifecycleEffect.PublishReplayUnavailable.Instance,
        IracingTelemetryLifecycleEffect.CloseReader.Instance,
        new IracingTelemetryLifecycleEffect.PublishTelemetry(TelemetryDisconnected.Instance));

    private static bool IsExpired(
        IracingTelemetryLifecycleState state,
        IracingLogicalConnectionAge age) =>
        (state.LogicalConnection, age) switch
        {
            (not null, IracingLogicalConnectionAge.Measured measured) =>
                measured.Elapsed >= IracingProtocol.ConnectionTimeout,
            (null, IracingLogicalConnectionAge.NotEstablished) => false,
            _ => throw new InvalidOperationException(
                "The logical-connection age does not match the telemetry lifecycle state."),
        };

    private static void EnsureNotEstablished(IracingLogicalConnectionAge age)
    {
        if (age is not IracingLogicalConnectionAge.NotEstablished)
        {
            throw new InvalidOperationException(
                "A telemetry lifecycle without an accepted sample cannot have a measured age.");
        }
    }

    private static void ValidateConnectionAge(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput input)
    {
        var age = input switch
        {
            IracingTelemetryLifecycleInput.ReaderOpenFailed failed =>
                failed.ConnectionAge,
            IracingTelemetryLifecycleInput.SampleAccepted accepted =>
                accepted.PreviousSampleAge,
            IracingTelemetryLifecycleInput.SampleRejected rejected =>
                rejected.PreviousSampleAge,
            IracingTelemetryLifecycleInput.NoData noData => noData.ConnectionAge,
            IracingTelemetryLifecycleInput.InvalidFrame invalid => invalid.ConnectionAge,
            _ => null,
        };
        if (age is null)
        {
            return;
        }

        var hasAcceptedSample = state.LogicalConnection is not null;
        if (hasAcceptedSample != (age is IracingLogicalConnectionAge.Measured))
        {
            throw new InvalidOperationException(
                "The logical-connection age does not match the telemetry lifecycle state.");
        }
    }

    private static IracingTelemetryLifecycleTransition Move(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLoopDirective directive = IracingTelemetryLoopDirective.Continue,
        params IracingTelemetryLifecycleEffect[] effects)
    {
        var readerMustBeOpen = directive is
            IracingTelemetryLoopDirective.Continue or
            IracingTelemetryLoopDirective.WaitForData;
        if (readerMustBeOpen !=
            (state.ReaderOwnership == IracingReaderOwnership.Open))
        {
            throw new InvalidOperationException(
                "The loop directive does not match the next state's reader ownership.");
        }

        var replayUnavailable = false;
        var readerClosed = false;
        var telemetryPublished = false;
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case IracingTelemetryLifecycleEffect.PublishReplayUnavailable:
                    replayUnavailable = true;
                    break;
                case IracingTelemetryLifecycleEffect.PublishReplayFrame when readerClosed:
                    throw new InvalidOperationException(
                        "Replay state cannot become available after the reader closes.");
                case IracingTelemetryLifecycleEffect.PublishReplayFrame:
                    replayUnavailable = false;
                    break;
                case IracingTelemetryLifecycleEffect.CloseReader
                    when readerClosed ||
                         !replayUnavailable ||
                         telemetryPublished ||
                         state.ReaderOwnership != IracingReaderOwnership.Closed:
                    throw new InvalidOperationException(
                        "Closing the reader requires one close after the latest replay " +
                        "invalidation, before fallible telemetry publication, and a closed " +
                        "next state.");
                case IracingTelemetryLifecycleEffect.CloseReader:
                    readerClosed = true;
                    break;
                case IracingTelemetryLifecycleEffect.PublishTelemetry:
                    telemetryPublished = true;
                    break;
            }
        }

        return new IracingTelemetryLifecycleTransition(
            state,
            Array.AsReadOnly(effects),
            directive);
    }

    private static InvalidOperationException InvalidTransition(
        IracingTelemetryLifecycleState state,
        IracingTelemetryLifecycleInput input) => new(
            $"Telemetry input '{input.GetType().Name}' is invalid while in " +
            $"state '{state.GetType().Name}'.");

    private static InvalidOperationException InvalidStateForReader(
        IracingTelemetryLifecycleState state) => new(
            $"Telemetry state '{state.GetType().Name}' does not own an open reader.");
}
