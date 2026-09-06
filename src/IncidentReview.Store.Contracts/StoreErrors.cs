using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>Stable safe failures shared by every store implementation.</summary>
public static class StoreErrors
{
    /// <summary>Gets the failure returned before successful store initialization.</summary>
    public static Error NotInitialized { get; } = Error.Create(
        StoreErrorCodes.NotInitialized,
        ErrorKind.Unavailable,
        "The store is not initialized.");

    /// <summary>Gets the failure returned for an unsupported typed request.</summary>
    public static Error UnsupportedRequest { get; } = Error.Create(
        StoreErrorCodes.UnsupportedRequest,
        ErrorKind.Validation,
        "The store request is unsupported.");

    /// <summary>Gets the safe failure returned for a provider operation that did not commit.</summary>
    public static Error PersistenceFailure { get; } = Error.Create(
        StoreErrorCodes.PersistenceFailure,
        ErrorKind.Persistence,
        "The store operation failed.");

    /// <summary>Gets the failure returned when an operation identity is reused for different content.</summary>
    public static Error OperationIdConflict { get; } = Error.Create(
        StoreErrorCodes.OperationIdConflict,
        ErrorKind.Conflict,
        "The operation identifier is already bound to different command content.");

    /// <summary>Gets the failure returned when a commit outcome cannot be proven.</summary>
    public static Error IndeterminateCommit { get; } = Error.Create(
        StoreErrorCodes.IndeterminateCommit,
        ErrorKind.Indeterminate,
        "The store could not determine whether the operation committed.");
}
