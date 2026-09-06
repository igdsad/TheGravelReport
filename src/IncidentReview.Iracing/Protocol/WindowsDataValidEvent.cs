using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IncidentReview.Iracing.Protocol;

internal sealed partial class WindowsDataValidEvent : WaitHandle
{
    private const uint SynchronizeAccess = 0x00100000;

    private WindowsDataValidEvent(SafeWaitHandle handle)
    {
        SafeWaitHandle = handle;
    }

    public static bool TryOpen(
        string name,
        [NotNullWhen(true)] out WindowsDataValidEvent? dataEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var nativeHandle = OpenEventW(SynchronizeAccess, inheritHandle: 0, name);
        if (nativeHandle == 0)
        {
            dataEvent = null;
            return false;
        }

        dataEvent = new WindowsDataValidEvent(new SafeWaitHandle(
            nativeHandle,
            ownsHandle: true));
        return true;
    }

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "OpenEventW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenEventW(
        uint desiredAccess,
        int inheritHandle,
        string name);
}
