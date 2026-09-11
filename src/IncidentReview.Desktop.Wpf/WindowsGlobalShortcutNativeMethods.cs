using System.Runtime.InteropServices;

namespace IncidentReview.Desktop.Wpf;

internal interface IGlobalShortcutNativeMethods
{
    public bool TryRegister(nint windowHandle, int id, uint modifiers, uint virtualKey);

    public bool TryUnregister(nint windowHandle, int id);
}

internal sealed class WindowsGlobalShortcutNativeMethods : IGlobalShortcutNativeMethods
{
    public bool TryRegister(nint windowHandle, int id, uint modifiers, uint virtualKey) =>
        RegisterHotKey(windowHandle, id, modifiers, virtualKey) != 0;

    public bool TryUnregister(nint windowHandle, int id) =>
        UnregisterHotKey(windowHandle, id) != 0;

    // Desktop.Wpf deliberately does not enable unsafe compilation. These fixed,
    // blittable signatures therefore use classic P/Invoke instead of source-generated
    // LibraryImport declarations, which require AllowUnsafeBlocks.
#pragma warning disable SYSLIB1054 // Use LibraryImportAttribute
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegisterHotKey(
        nint windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int UnregisterHotKey(nint windowHandle, int id);
#pragma warning restore SYSLIB1054 // Use LibraryImportAttribute
}
