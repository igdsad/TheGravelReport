using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts;

/// <summary>Validated addresses and session identity used to start a joinable server.</summary>
public sealed record CustomEventSessionHostRequest
{
    private CustomEventSessionHostRequest(
        SessionIdentity sessionIdentity,
        Uri listenUri,
        Uri advertisedBaseUri)
    {
        SessionIdentity = sessionIdentity;
        ListenUri = listenUri;
        AdvertisedBaseUri = advertisedBaseUri;
    }

    /// <summary>Gets the deterministic session accepted by this host.</summary>
    public SessionIdentity SessionIdentity { get; }

    /// <summary>Gets the local root address on which the adapter listens.</summary>
    public Uri ListenUri { get; }

    /// <summary>Gets the externally reachable base address embedded in the join code.</summary>
    public Uri AdvertisedBaseUri { get; }

    /// <summary>Validates a joinable-session host request.</summary>
    public static Result<CustomEventSessionHostRequest> TryCreate(
        SessionIdentity? sessionIdentity,
        Uri? listenUri,
        Uri? advertisedBaseUri)
    {
        if (sessionIdentity is null ||
            !EventSyncUri.TryNormalizeListen(listenUri, out var normalizedListenUri))
        {
            return Result<CustomEventSessionHostRequest>.Failure(
                EventSyncErrors.InvalidHostRequest);
        }

        var joinCode = JoinCode.TryCreate(advertisedBaseUri, sessionIdentity);
        if (!joinCode.IsSuccess)
        {
            return Result<CustomEventSessionHostRequest>.Failure(
                EventSyncErrors.InvalidHostRequest);
        }

        return Result<CustomEventSessionHostRequest>.Success(
            new CustomEventSessionHostRequest(
                sessionIdentity,
                normalizedListenUri,
                joinCode.Value.ServerBaseUri));
    }
}
