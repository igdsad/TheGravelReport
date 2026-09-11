using System.Buffers.Binary;
using System.Text;
using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts;

/// <summary>
/// Carries the advertised event-sync server address and one stable session identity.
/// </summary>
/// <remarks>
/// The encoded representation is versioned, canonical Base64Url text. It is a locator,
/// not a credential, and deliberately provides no authentication or secrecy.
/// </remarks>
public sealed record JoinCode
{
    private const byte FormatVersion = 1;
    private const string Prefix = "grv1_";
    private const int HeaderLength = 3;
    private const int GuidLength = 16;
    private const int MaximumEncodedLength = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private JoinCode(string value, Uri serverBaseUri, SessionIdentity sessionIdentity)
    {
        Value = value;
        ServerBaseUri = serverBaseUri;
        SessionIdentity = sessionIdentity;
    }

    /// <summary>Gets the canonical, versioned code suitable for copy and paste.</summary>
    public string Value { get; }

    /// <summary>Gets the current encoded join-code format version.</summary>
    public static int CurrentVersion => FormatVersion;

    /// <summary>Gets the advertised HTTP or HTTPS server base address.</summary>
    public Uri ServerBaseUri { get; }

    /// <summary>Gets the deterministic review-session identity selected by the host.</summary>
    public SessionIdentity SessionIdentity { get; }

    /// <summary>Creates a canonical join code from validated session and server values.</summary>
    public static Result<JoinCode> TryCreate(
        Uri? serverBaseUri,
        SessionIdentity? sessionIdentity)
    {
        if (sessionIdentity is null ||
            !IsVersion5(sessionIdentity.Value) ||
            !EventSyncUri.TryNormalizeBase(serverBaseUri, out var normalizedUri))
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        var uriBytes = StrictUtf8.GetBytes(normalizedUri.AbsoluteUri);
        if (uriBytes.Length > ushort.MaxValue)
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        var payload = new byte[HeaderLength + uriBytes.Length + GuidLength];
        payload[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1, 2), (ushort)uriBytes.Length);
        uriBytes.CopyTo(payload, HeaderLength);
        if (!sessionIdentity.Value.TryWriteBytes(
                payload.AsSpan(HeaderLength + uriBytes.Length, GuidLength),
                bigEndian: true,
                out var bytesWritten) ||
            bytesWritten != GuidLength)
        {
            throw new InvalidOperationException("A session identity did not fit its UUID encoding.");
        }

        var encoded = Prefix + EncodeBase64Url(payload);
        if (encoded.Length > MaximumEncodedLength)
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        return Result<JoinCode>.Success(new JoinCode(
            encoded,
            normalizedUri,
            sessionIdentity));
    }

    /// <summary>Parses and validates a canonical versioned join code.</summary>
    public static Result<JoinCode> TryParse(string? text)
    {
        if (string.IsNullOrEmpty(text) ||
            text.Length > MaximumEncodedLength ||
            !text.StartsWith(Prefix, StringComparison.Ordinal) ||
            !TryDecodeBase64Url(text.AsSpan(Prefix.Length), out var payload) ||
            payload.Length < HeaderLength + GuidLength ||
            payload[0] != FormatVersion)
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        var uriLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1, 2));
        if (payload.Length != HeaderLength + uriLength + GuidLength)
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        string uriText;
        try
        {
            uriText = StrictUtf8.GetString(payload, HeaderLength, uriLength);
        }
        catch (DecoderFallbackException)
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        var sessionGuid = new Guid(
            payload.AsSpan(HeaderLength + uriLength, GuidLength),
            bigEndian: true);
        var session = SessionIdentity.TryCreate(sessionGuid);
        if (!session.IsSuccess)
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        var recreated = TryCreate(uri, session.Value);
        if (!recreated.IsSuccess ||
            !string.Equals(recreated.Value.Value, text, StringComparison.Ordinal))
        {
            return Result<JoinCode>.Failure(EventSyncErrors.InvalidJoinCode);
        }

        return recreated;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static string EncodeBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool IsVersion5(Guid value)
    {
        Span<byte> bytes = stackalloc byte[GuidLength];
        return value.TryWriteBytes(bytes, bigEndian: true, out var bytesWritten) &&
               bytesWritten == GuidLength &&
               (bytes[6] & 0xf0) == 0x50 &&
               (bytes[8] & 0xc0) == 0x80;
    }

    private static bool TryDecodeBase64Url(ReadOnlySpan<char> encoded, out byte[] bytes)
    {
        bytes = [];
        if (encoded.IsEmpty || encoded.Length % 4 == 1)
        {
            return false;
        }

        foreach (var character in encoded)
        {
            if (!((character is >= 'A' and <= 'Z') ||
                  (character is >= 'a' and <= 'z') ||
                  (character is >= '0' and <= '9') ||
                  character is '-' or '_'))
            {
                return false;
            }
        }

        var standard = encoded.ToString().Replace('-', '+').Replace('_', '/');
        standard += (standard.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        try
        {
            bytes = Convert.FromBase64String(standard);
            return string.Equals(
                EncodeBase64Url(bytes),
                encoded.ToString(),
                StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
