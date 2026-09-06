using System.Runtime.InteropServices;
using IncidentReview.Iracing.Protocol;

namespace IncidentReview.Iracing.Replay;

internal sealed partial class WindowsReplayMessageSender : IReplayMessageSender
{
    private static readonly nint BroadcastWindow = (nint)0xffff;
    private readonly string _messageName;

    public WindowsReplayMessageSender()
        : this(IracingProtocol.BroadcastMessageName)
    {
    }

    internal WindowsReplayMessageSender(string messageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);
        _messageName = messageName;
    }

    public ReplaySendOutcome Send(ReplayBroadcastCommand command)
    {
        try
        {
            var messageId = RegisterWindowMessageW(_messageName);
            if (messageId == 0)
            {
                return ReplaySendOutcome.EndpointUnavailable;
            }

            return SendNotifyMessageW(
                    BroadcastWindow,
                    messageId,
                    unchecked((nuint)(uint)command.WParam),
                    command.LParam) != 0
                ? ReplaySendOutcome.Delivered
                : ReplaySendOutcome.DeliveryRejected;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return ReplaySendOutcome.EndpointUnavailable;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string messageName);

    [LibraryImport("user32.dll", EntryPoint = "SendNotifyMessageW", SetLastError = true)]
    private static partial int SendNotifyMessageW(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);
}
