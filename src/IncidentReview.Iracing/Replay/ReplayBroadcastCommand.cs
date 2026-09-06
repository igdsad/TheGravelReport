namespace IncidentReview.Iracing.Replay;

internal readonly record struct ReplayBroadcastCommand(
    Iracing.Protocol.IracingBroadcastMessage Message,
    int WParam,
    int LParam);

internal enum ReplaySendOutcome
{
    Delivered,
    EndpointUnavailable,
    DeliveryRejected,
}

internal interface IReplayMessageSender
{
    public ReplaySendOutcome Send(ReplayBroadcastCommand command);
}
