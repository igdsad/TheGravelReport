using IncidentReview.Results;

namespace IncidentReview.Domain;

/// <summary>Describes how long simulator identity evidence can be trusted.</summary>
public sealed record SimulatorIdentityScope
{
    private const int DurableValue = 1;
    private const int ConnectionScopedValue = 2;

    private static readonly Error InvalidError = DomainValidationError.Create(
        "domain.simulator-identity-scope.invalid",
        "The simulator identity scope code is unknown.");

    private SimulatorIdentityScope(int value)
    {
        Value = value;
    }

    /// <summary>Gets cross-reconnect/restart identity scope. Its stable persisted value is 1.</summary>
    public static SimulatorIdentityScope Durable { get; } = new(DurableValue);

    /// <summary>Gets current-connection identity scope. Its stable persisted value is 2.</summary>
    public static SimulatorIdentityScope ConnectionScoped { get; } = new(ConnectionScopedValue);

    /// <summary>Gets the stable persisted integer code.</summary>
    public int Value { get; }

    /// <summary>Validates a persisted integer code.</summary>
    public static Result<SimulatorIdentityScope> TryCreate(int value) => value switch
    {
        DurableValue => Result<SimulatorIdentityScope>.Success(Durable),
        ConnectionScopedValue => Result<SimulatorIdentityScope>.Success(ConnectionScoped),
        _ => Result<SimulatorIdentityScope>.Failure(InvalidError),
    };

    /// <inheritdoc />
    public override string ToString() =>
        Value == DurableValue ? nameof(Durable) : nameof(ConnectionScoped);
}
