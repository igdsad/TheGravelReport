using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace IncidentReview.Domain;

/// <summary>Creates RFC 4122 name-based UUIDs using SHA-1.</summary>
internal static class Uuid5
{
    public static bool IsValid(Guid value) =>
        value != Guid.Empty &&
        value.Version == 5 &&
        value.Variant is >= 8 and <= 11;

    [SuppressMessage(
        "Security",
        "CA5350:Do not use weak cryptographic algorithms",
        Justification = "RFC 4122 UUID version 5 requires SHA-1 for stable naming, not for cryptographic security.")]
    public static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var namespaceBytes = namespaceId.ToByteArray(bigEndian: true);
        var nameByteCount = Encoding.UTF8.GetByteCount(name);
        var input = new byte[namespaceBytes.Length + nameByteCount];
        namespaceBytes.CopyTo(input, 0);
        Encoding.UTF8.GetBytes(name, input.AsSpan(16));

        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
