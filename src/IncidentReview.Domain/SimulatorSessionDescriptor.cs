using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>
/// Carries validated simulator-owned evidence used by the application to resolve a session.
/// </summary>
public sealed record SimulatorSessionDescriptor
{
    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.simulator-session-descriptor.invalid",
        "A simulator session descriptor requires all identity components.");

    private SimulatorSessionDescriptor(
        SimulatorCode simulator,
        SimulatorSessionKey sessionKey,
        SessionNumber sessionNumber,
        SessionMode mode,
        SimulatorIdentityScope identityScope)
    {
        Simulator = simulator;
        SessionKey = sessionKey;
        SessionNumber = sessionNumber;
        Mode = mode;
        IdentityScope = identityScope;
    }

    /// <summary>Gets the canonical simulator code.</summary>
    public SimulatorCode Simulator { get; }

    /// <summary>Gets the opaque simulator-owned identity evidence.</summary>
    public SimulatorSessionKey SessionKey { get; }

    /// <summary>Gets the simulator session number.</summary>
    public SessionNumber SessionNumber { get; }

    /// <summary>Gets whether the session is live or replayed.</summary>
    public SessionMode Mode { get; }

    /// <summary>Gets how long the identity evidence can be trusted.</summary>
    public SimulatorIdentityScope IdentityScope { get; }

    /// <summary>Combines validated identity components.</summary>
    public static Result<SimulatorSessionDescriptor> TryCreate(
        SimulatorCode? simulator,
        SimulatorSessionKey? sessionKey,
        SessionNumber? sessionNumber,
        SessionMode? mode,
        SimulatorIdentityScope? identityScope) =>
        simulator is not null &&
        sessionKey is not null &&
        sessionNumber is not null &&
        mode is not null &&
        identityScope is not null
            ? Result<SimulatorSessionDescriptor>.Success(
                new SimulatorSessionDescriptor(
                    simulator,
                    sessionKey,
                    sessionNumber,
                    mode,
                    identityScope))
            : Result<SimulatorSessionDescriptor>.Failure(InvalidError);
}
