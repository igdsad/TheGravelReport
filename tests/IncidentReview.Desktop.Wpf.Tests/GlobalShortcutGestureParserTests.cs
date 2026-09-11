using System.Reflection;
using System.Windows.Input;

namespace IncidentReview.Desktop.Wpf.Tests;

[TestClass]
public sealed class GlobalShortcutGestureParserTests
{
    private static readonly MethodInfo ParserMethod = typeof(IGlobalShortcut).Assembly
        .GetType(
            "IncidentReview.Desktop.Wpf.GlobalShortcutGestureParser",
            throwOnError: true,
            ignoreCase: false)!
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(static method =>
            string.Equals(method.Name, "TryParse", StringComparison.Ordinal) &&
            method.GetParameters() is [var text, var parsed] &&
            text.ParameterType == typeof(string) &&
            parsed.IsOut);

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-003")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void WpfGestureTextMapsToOneNoRepeatWindowsHotKey()
    {
        var parsed = Parse("  Ctrl+Alt+Shift+F9  ");

        Assert.IsTrue(parsed.Success);
        Assert.AreEqual(Key.F9, ReadProperty<Key>(parsed.Value!, "Key"));
        Assert.AreEqual(
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift,
            ReadProperty<ModifierKeys>(parsed.Value!, "Modifiers"));
        Assert.AreEqual(
            unchecked((uint)KeyInterop.VirtualKeyFromKey(Key.F9)),
            ReadProperty<uint>(parsed.Value!, "VirtualKey"));
        Assert.AreEqual(0x4007U, ReadProperty<uint>(parsed.Value!, "NativeModifiers"));

        var unmodified = Parse("F9");
        Assert.IsTrue(unmodified.Success);
        Assert.AreEqual(0x4000U, ReadProperty<uint>(unmodified.Value!, "NativeModifiers"));
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-EVT-003")]
    [TestProperty("Requirement", "QR-ERR-001")]
    [TestProperty("Requirement", "QR-TST-001")]
    public void BlankMalformedAndModifierOnlyGesturesAreRejected()
    {
        string[] invalidGestures =
        [
            string.Empty,
            "   ",
            "Ctrl+DefinitelyNotAKey",
            "Ctrl+Alt",
            "Ctrl",
            "LeftShift",
            "Win",
        ];

        foreach (var invalidGesture in invalidGestures)
        {
            Assert.IsFalse(Parse(invalidGesture).Success, invalidGesture);
        }
    }

    private static ParseResult Parse(string text)
    {
        object?[] arguments = [text, null];
        var success = ParserMethod.Invoke(obj: null, arguments) as bool? ?? false;
        return new ParseResult(success, arguments[1]);
    }

    private static T ReadProperty<T>(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(value) is T typed
            ? typed
            : throw new InvalidOperationException($"Parsed gesture has no {propertyName} value.");
    }

    private sealed record ParseResult(bool Success, object? Value);
}
