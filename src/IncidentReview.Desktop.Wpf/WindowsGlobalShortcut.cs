using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using IncidentReview.Results;

namespace IncidentReview.Desktop.Wpf;

internal sealed class WindowsGlobalShortcut : IGlobalShortcut
{
    private const int HotKeyMessage = 0x0312;
    private const int HotKeyAlreadyRegisteredError = 1409;
    private const int FirstHotKeyId = 0x1000;
    private const int LastHotKeyId = 0xbfff;

    private static readonly Error InvalidGestureError = Error.Create(
        ErrorCode.Define("desktop.global-shortcut.invalid-gesture"),
        ErrorKind.Validation,
        "The custom-event shortcut is not a valid keyboard gesture.");

    private static readonly Error RegistrationConflictError = Error.Create(
        ErrorCode.Define("desktop.global-shortcut.registration-conflict"),
        ErrorKind.Conflict,
        "The custom-event shortcut is already in use by another application.");

    private static readonly Error RegistrationUnavailableError = Error.Create(
        ErrorCode.Define("desktop.global-shortcut.registration-unavailable"),
        ErrorKind.Unavailable,
        "Windows could not register the custom-event shortcut.");

    private static int s_nextHotKeyId = FirstHotKeyId - 1;

    private readonly object _gate = new();
    private readonly IGlobalShortcutNativeMethods _nativeMethods;
    private readonly HwndSourceHook _windowMessageHook;
    private EventHandler? _activated;
    private Window? _owner;
    private HwndSource? _source;
    private Dispatcher? _dispatcher;
    private nint _windowHandle;
    private int _hotKeyId;
    private ParsedGlobalShortcut _gesture;
    private bool _isRegistered;
    private bool _isDisposed;

    public WindowsGlobalShortcut()
        : this(new WindowsGlobalShortcutNativeMethods())
    {
    }

    internal WindowsGlobalShortcut(IGlobalShortcutNativeMethods nativeMethods)
    {
        ArgumentNullException.ThrowIfNull(nativeMethods);
        _nativeMethods = nativeMethods;
        _windowMessageHook = ProcessWindowMessage;
    }

    public event EventHandler? Activated
    {
        add
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _activated += value;
            }
        }

        remove
        {
            lock (_gate)
            {
                _activated -= value;
            }
        }
    }

    public Result Register(Window owner, string gestureText)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(gestureText);

        if (!GlobalShortcutGestureParser.TryParse(gestureText, out var gesture))
        {
            return Result.Failure(InvalidGestureError);
        }

        owner.Dispatcher.VerifyAccess();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isRegistered && ReferenceEquals(owner, _owner) && gesture == _gesture)
            {
                return Result.Success();
            }
        }

        if (!TryGetWindowSource(owner, out var windowHandle, out var source))
        {
            return Result.Failure(RegistrationUnavailableError);
        }

        var hotKeyId = Interlocked.Increment(ref s_nextHotKeyId);
        if (hotKeyId > LastHotKeyId)
        {
            return Result.Failure(RegistrationUnavailableError);
        }

        HwndSource? previousSource;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            previousSource = _source;
        }

        var addedHook = !ReferenceEquals(source, previousSource);
        if (addedHook && !TryAddHook(source))
        {
            return Result.Failure(RegistrationUnavailableError);
        }

        bool registered;
        try
        {
            registered = _nativeMethods.TryRegister(
                windowHandle,
                hotKeyId,
                gesture.NativeModifiers,
                gesture.VirtualKey);
        }
        catch (Exception exception) when (IsUnavailableNativeException(exception))
        {
            if (addedHook)
            {
                TryRemoveHook(source);
            }

            return Result.Failure(RegistrationUnavailableError);
        }

        if (!registered)
        {
            var lastError = Marshal.GetLastPInvokeError();
            if (addedHook)
            {
                TryRemoveHook(source);
            }

            return Result.Failure(lastError == HotKeyAlreadyRegisteredError
                ? RegistrationConflictError
                : RegistrationUnavailableError);
        }

        ReplaceRegistration(owner, source, windowHandle, hotKeyId, gesture);
        return Result.Success();
    }

    public void Unregister()
    {
        Dispatcher? dispatcher;
        lock (_gate)
        {
            if (_isDisposed || !_isRegistered)
            {
                return;
            }

            dispatcher = _dispatcher;
        }

        dispatcher?.VerifyAccess();
        UnregisterCore();
    }

    public void Dispose()
    {
        Dispatcher? dispatcher;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _activated = null;
            dispatcher = _dispatcher;
        }

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            UnregisterCore();
            return;
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            AbandonRegistration();
            return;
        }

        try
        {
            dispatcher.Invoke(UnregisterCore);
        }
        catch (TaskCanceledException)
        {
            AbandonRegistration();
        }
        catch (InvalidOperationException)
        {
            AbandonRegistration();
        }
    }

    private static bool TryGetWindowSource(
        Window owner,
        out nint windowHandle,
        out HwndSource source)
    {
        try
        {
            windowHandle = new WindowInteropHelper(owner).EnsureHandle();
            var resolved = HwndSource.FromHwnd(windowHandle);
            if (windowHandle == 0 || resolved is null || resolved.IsDisposed)
            {
                source = null!;
                return false;
            }

            source = resolved;
            return true;
        }
        catch (InvalidOperationException)
        {
            windowHandle = 0;
            source = null!;
            return false;
        }
    }

    private void ReplaceRegistration(
        Window owner,
        HwndSource source,
        nint windowHandle,
        int hotKeyId,
        ParsedGlobalShortcut gesture)
    {
        Window? previousOwner;
        HwndSource? previousSource;
        nint previousWindowHandle;
        int previousHotKeyId;
        bool hadPreviousRegistration;

        lock (_gate)
        {
            if (_isDisposed)
            {
                TryUnregisterNative(windowHandle, hotKeyId);
                TryRemoveHook(source);
                throw new ObjectDisposedException(nameof(WindowsGlobalShortcut));
            }

            previousOwner = _owner;
            previousSource = _source;
            previousWindowHandle = _windowHandle;
            previousHotKeyId = _hotKeyId;
            hadPreviousRegistration = _isRegistered;

            _owner = owner;
            _source = source;
            _dispatcher = owner.Dispatcher;
            _windowHandle = windowHandle;
            _hotKeyId = hotKeyId;
            _gesture = gesture;
            _isRegistered = true;
        }

        if (!ReferenceEquals(previousOwner, owner))
        {
            if (previousOwner is not null)
            {
                previousOwner.Closed -= OnOwnerClosed;
            }

            owner.Closed += OnOwnerClosed;
        }

        if (hadPreviousRegistration)
        {
            TryUnregisterNative(previousWindowHandle, previousHotKeyId);
        }

        if (previousSource is not null && !ReferenceEquals(previousSource, source))
        {
            TryRemoveHook(previousSource);
        }
    }

    private nint ProcessWindowMessage(
        nint windowHandle,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        _ = windowHandle;
        _ = lParam;
        EventHandler? activated;
        lock (_gate)
        {
            if (_isDisposed ||
                !_isRegistered ||
                message != HotKeyMessage ||
                wParam != _hotKeyId)
            {
                return 0;
            }

            handled = true;
            activated = _activated;
        }

        activated?.Invoke(this, EventArgs.Empty);
        return 0;
    }

    private void OnOwnerClosed(object? sender, EventArgs e)
    {
        _ = e;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _owner))
            {
                return;
            }
        }

        UnregisterCore();
    }

    private void UnregisterCore()
    {
        Window? owner;
        HwndSource? source;
        nint windowHandle;
        int hotKeyId;
        lock (_gate)
        {
            if (!_isRegistered)
            {
                return;
            }

            owner = _owner;
            source = _source;
            windowHandle = _windowHandle;
            hotKeyId = _hotKeyId;
            ClearRegistration();
        }

        if (owner is not null)
        {
            owner.Closed -= OnOwnerClosed;
        }

        TryUnregisterNative(windowHandle, hotKeyId);
        if (source is not null)
        {
            TryRemoveHook(source);
        }
    }

    private void AbandonRegistration()
    {
        nint windowHandle;
        int hotKeyId;
        lock (_gate)
        {
            if (!_isRegistered)
            {
                return;
            }

            windowHandle = _windowHandle;
            hotKeyId = _hotKeyId;
            ClearRegistration();
        }

        TryUnregisterNative(windowHandle, hotKeyId);
    }

    private void ClearRegistration()
    {
        _owner = null;
        _source = null;
        _dispatcher = null;
        _windowHandle = 0;
        _hotKeyId = 0;
        _gesture = default;
        _isRegistered = false;
    }

    private bool TryAddHook(HwndSource source)
    {
        try
        {
            source.AddHook(_windowMessageHook);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void TryRemoveHook(HwndSource source)
    {
        try
        {
            if (!source.IsDisposed)
            {
                source.RemoveHook(_windowMessageHook);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void TryUnregisterNative(nint windowHandle, int hotKeyId)
    {
        try
        {
            _ = _nativeMethods.TryUnregister(windowHandle, hotKeyId);
        }
        catch (Exception exception) when (IsUnavailableNativeException(exception))
        {
        }
    }

    private static bool IsUnavailableNativeException(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
}
