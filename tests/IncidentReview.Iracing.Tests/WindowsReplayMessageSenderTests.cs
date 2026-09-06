using System.Runtime.InteropServices;
using IncidentReview.Iracing.Testing;
using Forms = System.Windows.Forms;

namespace IncidentReview.Iracing.Tests;

[TestClass]
[DoNotParallelize]
public sealed partial class WindowsReplayMessageSenderTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-RPY-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void ProductionSenderDeliversExactGoldenVectorToHiddenNativeWindow()
    {
        var messageName = $"IncidentReview.IRSDK_BROADCASTMSG.{Guid.CreateVersion7():N}";
        using var receiver = NativeBroadcastReceiver.Start(
            RegisterWindowMessageW(messageName));
        var expected = new IracingTestReplayMessage(
            Command: 12,
            WParam: 12 | (2 << 16),
            LParam: 12_345);

        var outcome = IracingTestingRegistration.SendWindowsReplayMessage(
            expected,
            messageName);

        Assert.AreEqual(IracingTestDeliveryMode.Delivered, outcome);
        Assert.IsTrue(
            receiver.Wait(TimeSpan.FromSeconds(2)),
            "The hidden top-level receiver did not receive the registered broadcast message.");
        Assert.AreEqual(unchecked((nuint)(uint)expected.WParam), receiver.WParam);
        Assert.AreEqual((nint)expected.LParam, receiver.LParam);
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string messageName);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    private sealed class NativeBroadcastReceiver : Forms.NativeWindow, IDisposable
    {
        private const uint QuitMessage = 0x0012;
        private readonly ManualResetEventSlim _received = new(initialState: false);
        private readonly uint _message;
        private readonly Thread _thread;
        private uint _threadId;
        private Exception? _startupError;
        private int _isDisposed;

        private NativeBroadcastReceiver(uint message)
        {
            _message = message;
            using var ready = new ManualResetEventSlim(initialState: false);
            _thread = new Thread(() => Run(ready))
            {
                IsBackground = true,
                Name = "iRacing replay broadcast test receiver",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(2)))
            {
                throw new InvalidOperationException("The hidden native receiver did not start.");
            }

            if (_startupError is not null)
            {
                throw new InvalidOperationException(
                    "The hidden native receiver could not create its window.",
                    _startupError);
            }
        }

        public nuint WParam { get; private set; }

        public nint LParam { get; private set; }

        public static NativeBroadcastReceiver Start(uint message)
        {
            if (message == 0)
            {
                throw new InvalidOperationException("The registered iRacing message is unavailable.");
            }

            return new NativeBroadcastReceiver(message);
        }

        public bool Wait(TimeSpan timeout) => _received.Wait(timeout);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            if (_threadId != 0)
            {
                _ = PostThreadMessageW(_threadId, QuitMessage, 0, 0);
            }

            _ = _thread.Join(TimeSpan.FromSeconds(2));
            _received.Dispose();
        }

        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == _message)
            {
                WParam = unchecked((nuint)message.WParam);
                LParam = message.LParam;
                _received.Set();
            }

            base.WndProc(ref message);
        }

        private void Run(ManualResetEventSlim ready)
        {
            try
            {
                _threadId = GetCurrentThreadId();
                CreateHandle(new Forms.CreateParams
                {
                    Caption = "IncidentReview hidden SDK receiver",
                });
            }
            catch (Exception exception)
            {
                _startupError = exception;
                ready.Set();
                return;
            }

            ready.Set();
            try
            {
                Forms.Application.Run();
            }
            finally
            {
                DestroyHandle();
            }
        }
    }
}
