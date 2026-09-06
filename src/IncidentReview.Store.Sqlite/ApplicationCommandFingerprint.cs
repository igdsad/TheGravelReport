using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IncidentReview.Domain;
using IncidentReview.Store.Contracts;

namespace IncidentReview.Store.Sqlite;

internal sealed record ApplicationCommandDescriptor(string Kind, int Version, byte[] Fingerprint);

internal static class ApplicationCommandFingerprint
{
    public static ApplicationCommandDescriptor Describe(IStoreCommand command)
    {
        var kind = command switch
        {
            EnsureSession => "session.ensure",
            EstablishIncidentCheckpoint => "incident-checkpoint.establish",
            RecordDetectedIncident => "incident.record-detected",
            AnnotateIncident => "incident.annotate",
            MarkIncidentReviewed => "incident.mark-reviewed",
            _ => throw new InvalidOperationException("The command has no application-store descriptor."),
        };
        var version = command is EstablishIncidentCheckpoint or RecordDetectedIncident ? 2 : 1;
        var writer = new ArrayBufferWriter<byte>();
        WriteString(writer, kind);
        WriteInt32(writer, version);
        switch (command)
        {
            case EnsureSession ensure:
                WriteString(writer, ensure.ProposedIdentity.ToString());
                WriteDescriptor(writer, ensure.Descriptor);
                WriteInt64(writer, ensure.StartedAt.UnixMilliseconds);
                break;
            case EstablishIncidentCheckpoint establish:
                WriteOptionalCheckpoint(writer, establish.ExpectedCheckpoint);
                WriteCheckpoint(writer, establish.NextCheckpoint);
                break;
            case RecordDetectedIncident record:
                WriteIncident(writer, record.Incident);
                WriteCheckpoint(writer, record.ExpectedCheckpoint);
                WriteCheckpoint(writer, record.NextCheckpoint);
                break;
            case AnnotateIncident annotate:
                WriteString(writer, annotate.Incident.ToString());
                WriteOptionalString(writer, annotate.Annotation.Notes);
                WriteOptionalInt32(writer, annotate.Annotation.Classification?.Value);
                WriteInt64(writer, annotate.UpdatedAt.UnixMilliseconds);
                break;
            case MarkIncidentReviewed reviewed:
                WriteString(writer, reviewed.Incident.ToString());
                WriteInt64(writer, reviewed.ReviewedAt.UnixMilliseconds);
                break;
        }

        return new ApplicationCommandDescriptor(kind, version, SHA256.HashData(writer.WrittenSpan));
    }

    private static void WriteDescriptor(ArrayBufferWriter<byte> writer, SimulatorSessionDescriptor value)
    {
        WriteString(writer, value.Simulator.Value);
        WriteString(writer, value.SessionKey.Value);
        WriteInt32(writer, value.IdentityScope.Value);
        WriteInt32(writer, value.SessionNumber.Value);
        WriteInt32(writer, value.Mode.Value);
    }

    private static void WriteIncident(ArrayBufferWriter<byte> writer, StoredIncident value)
    {
        WriteString(writer, value.Id.ToString());
        WriteString(writer, value.Session.ToString());
        WriteString(writer, value.Participant.Identity.Value);
        WriteOptionalString(writer, value.Participant.DriverName);
        WriteOptionalString(writer, value.Participant.TeamName);
        WriteOptionalString(writer, value.Participant.CarNumber);
        WriteInt32(writer, value.Position.SessionNumber.Value);
        WriteInt64(writer, value.Position.SessionTime.Milliseconds);
        WriteInt64(writer, value.ObservedAt.UnixMilliseconds);
        WriteInt32(writer, value.Points.Delta);
        WriteInt32(writer, value.Points.Total);
        WriteInt32(writer, value.CounterEpoch.Value);
        WriteOptionalInt32(writer, value.Lap?.Value);
        WriteOptionalInt64(writer, value.LapDistance is null
            ? null
            : BitConverter.DoubleToInt64Bits(value.LapDistance.Value));
        WriteInt32(writer, value.ReviewStatus.Value);
        WriteOptionalInt32(writer, value.Annotation.Classification?.Value);
        WriteOptionalString(writer, value.Annotation.Notes);
        WriteInt64(writer, value.CreatedAt.UnixMilliseconds);
        WriteInt64(writer, value.UpdatedAt.UnixMilliseconds);
    }

    private static void WriteOptionalCheckpoint(
        ArrayBufferWriter<byte> writer,
        IncidentCheckpoint? value)
    {
        WriteByte(writer, value is null ? (byte)0 : (byte)1);
        if (value is not null)
        {
            WriteCheckpoint(writer, value);
        }
    }

    private static void WriteCheckpoint(ArrayBufferWriter<byte> writer, IncidentCheckpoint value)
    {
        WriteString(writer, value.Session.ToString());
        WriteString(writer, value.ParticipantIdentity.Value);
        WriteInt32(writer, value.CounterEpoch.Value);
        WriteInt32(writer, value.LastCounter.Value);
        WriteInt32(writer, value.LastPosition.SessionNumber.Value);
        WriteInt64(writer, value.LastPosition.SessionTime.Milliseconds);
        WriteInt64(writer, value.UpdatedAt.UnixMilliseconds);
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value) =>
        WriteBytes(writer, Encoding.UTF8.GetBytes(value));

    private static void WriteOptionalString(ArrayBufferWriter<byte> writer, string? value)
    {
        if (value is null)
        {
            WriteLength(writer, -1);
            return;
        }

        WriteString(writer, value);
    }

    private static void WriteOptionalInt32(ArrayBufferWriter<byte> writer, int? value)
    {
        WriteByte(writer, value.HasValue ? (byte)1 : (byte)0);
        if (value.HasValue)
        {
            WriteInt32(writer, value.Value);
        }
    }

    private static void WriteOptionalInt64(ArrayBufferWriter<byte> writer, long? value)
    {
        WriteByte(writer, value.HasValue ? (byte)1 : (byte)0);
        if (value.HasValue)
        {
            WriteInt64(writer, value.Value);
        }
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
