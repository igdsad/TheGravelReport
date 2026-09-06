using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IncidentReview.Store.Contracts;

namespace IncidentReview.Store.Sqlite;

internal static class PreferencesCommandFingerprint
{
    public const string CommandKind = "preferences.update";
    public const int CommandVersion = 2;

    public static byte[] Create(UpdatePreferences command)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteUtf8(writer, CommandKind);
        WriteInt32(writer, CommandVersion);
        WriteInt64(writer, command.Preferences.ReplayLeadInMilliseconds);
        WriteByte(writer, command.Preferences.AutoPause ? (byte)1 : (byte)0);
        WriteInt64(writer, BitConverter.DoubleToInt64Bits(command.Preferences.PlaybackSpeed));
        WriteOptionalUtf8(writer, command.Preferences.PreferredCamera);
        WriteInt32(writer, (int)command.Preferences.Theme);
        WriteInt64(writer, command.UpdatedAt.UnixMilliseconds);
        return SHA256.HashData(writer.WrittenSpan);
    }

    private static void WriteUtf8(ArrayBufferWriter<byte> writer, string value) =>
        WriteBytes(writer, Encoding.UTF8.GetBytes(value));

    private static void WriteOptionalUtf8(ArrayBufferWriter<byte> writer, string? value)
    {
        if (value is null)
        {
            WriteLength(writer, -1);
            return;
        }

        WriteUtf8(writer, value);
    }

    private static void WriteInt32(ArrayBufferWriter<byte> writer, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        WriteBytes(writer, bytes);
    }

    private static void WriteInt64(ArrayBufferWriter<byte> writer, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        WriteBytes(writer, bytes);
    }

    private static void WriteByte(ArrayBufferWriter<byte> writer, byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        WriteBytes(writer, bytes);
    }

    private static void WriteBytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        WriteLength(writer, value.Length);
        value.CopyTo(writer.GetSpan(value.Length));
        writer.Advance(value.Length);
    }

    private static void WriteLength(ArrayBufferWriter<byte> writer, int length)
    {
        var destination = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(destination, length);
        writer.Advance(sizeof(int));
    }
}
