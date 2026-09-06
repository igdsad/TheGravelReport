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
}
