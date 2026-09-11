using System.Windows.Input;

namespace IncidentReview.Desktop.Wpf;

internal static class GlobalShortcutGestureParser
{
    private const ModifierKeys SupportedModifiers =
        ModifierKeys.Alt |
        ModifierKeys.Control |
        ModifierKeys.Shift |
        ModifierKeys.Windows;

    private const uint NativeAlt = 0x0001;
    private const uint NativeControl = 0x0002;
    private const uint NativeShift = 0x0004;
    private const uint NativeWindows = 0x0008;
    private const uint NativeNoRepeat = 0x4000;

    public static bool TryParse(string gestureText, out ParsedGlobalShortcut parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(gestureText))
        {
            return false;
        }

        KeyGesture? gesture;
        try
        {
            gesture = new KeyGestureConverter()
                .ConvertFromInvariantString(gestureText.Trim()) as KeyGesture;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (gesture is null ||
            gesture.Key == Key.None ||
            IsModifierKey(gesture.Key) ||
            (gesture.Modifiers & ~SupportedModifiers) != ModifierKeys.None)
        {
            return false;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(gesture.Key);
        if (virtualKey <= 0)
        {
            return false;
        }

        var nativeModifiers = NativeNoRepeat;
        if ((gesture.Modifiers & ModifierKeys.Alt) != 0)
        {
            nativeModifiers |= NativeAlt;
        }

        if ((gesture.Modifiers & ModifierKeys.Control) != 0)
        {
            nativeModifiers |= NativeControl;
        }

        if ((gesture.Modifiers & ModifierKeys.Shift) != 0)
        {
            nativeModifiers |= NativeShift;
        }

        if ((gesture.Modifiers & ModifierKeys.Windows) != 0)
        {
            nativeModifiers |= NativeWindows;
        }

        parsed = new ParsedGlobalShortcut(
            gesture.Key,
            gesture.Modifiers,
            unchecked((uint)virtualKey),
            nativeModifiers);
        return true;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftAlt or
        Key.RightAlt or
        Key.LeftCtrl or
        Key.RightCtrl or
        Key.LeftShift or
        Key.RightShift or
        Key.LWin or
        Key.RWin;
}

internal readonly record struct ParsedGlobalShortcut(
    Key Key,
    ModifierKeys Modifiers,
    uint VirtualKey,
    uint NativeModifiers);
