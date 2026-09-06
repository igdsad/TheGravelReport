using IncidentReview.Results;

namespace IncidentReview.Store.Contracts;

/// <summary>
/// Stable error codes owned by the store contract boundary.
/// </summary>
public static class StoreErrorCodes
{
    /// <summary>
    /// The supplied operation identifier is not a canonical UUIDv7 value.
    /// </summary>
    public static ErrorCode InvalidOperationId { get; } =
        ErrorCode.Define("store.operation-id.invalid");

    /// <summary>The store has not successfully completed initialization.</summary>
    public static ErrorCode NotInitialized { get; } =
        ErrorCode.Define("store.not-initialized");

    /// <summary>The selected store implementation does not support the request.</summary>
    public static ErrorCode UnsupportedRequest { get; } =
        ErrorCode.Define("store.request.unsupported");

    /// <summary>The store could not complete an operation before commit.</summary>
    public static ErrorCode PersistenceFailure { get; } =
        ErrorCode.Define("store.persistence-failed");

    /// <summary>An operation identifier is already bound to different command content.</summary>
    public static ErrorCode OperationIdConflict { get; } =
        ErrorCode.Define("store.operation-id.conflict");

    /// <summary>The store cannot prove whether the command committed.</summary>
    public static ErrorCode IndeterminateCommit { get; } =
        ErrorCode.Define("store.commit.indeterminate");
}
