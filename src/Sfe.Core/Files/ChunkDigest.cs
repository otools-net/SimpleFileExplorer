using System.Security.Cryptography;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The SHA-256 a chunk of an upload says it has: <c>Content-Digest: sha-256=:&lt;base64&gt;:</c> (RFC 9530; other
/// algorithms in the header are ignored). The server checks the bytes it received against it before they count, so
/// bytes damaged on the way (a proxy, a broken network card) are sent again instead of landing in the file. A chunk
/// without the header is taken as before (clients older than 2.0).
/// </summary>
public static class ChunkDigest
{
    public const string Header = "Content-Digest";

    private const string Algorithm = "sha-256";

    /// <summary>
    /// Reads the header: true with null when there is none or it has no SHA-256, true with the 32 bytes, false when
    /// its SHA-256 can't be read.
    /// </summary>
    public static bool TryParse(string? header, out byte[]? sha256)
    {
        sha256 = null;
        if (string.IsNullOrWhiteSpace(header))
            return true;
        foreach (var member in header.Split(','))
        {
            var equals = member.IndexOf('=');
            if (equals < 0 || !member[..equals].Trim().Equals(Algorithm, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = member[(equals + 1)..].Trim();
            if (value.Length < 2 || value[0] != ':' || value[^1] != ':')
                return false;
            var bytes = new byte[SHA256.HashSizeInBytes];
            if (!Convert.TryFromBase64String(value[1..^1], bytes, out var written) || written != bytes.Length)
                return false;
            sha256 = bytes;
            return true;
        }
        return true;
    }

    /// <summary>Whether <paramref name="actual"/> is the digest the chunk said it has.</summary>
    public static bool Matches(byte[] expected, ReadOnlySpan<byte> actual) =>
        CryptographicOperations.FixedTimeEquals(expected, actual);

    /// <summary>The header's value for <paramref name="content"/> (tests, tools).</summary>
    public static string Of(ReadOnlySpan<byte> content) =>
        $"{Algorithm}=:{Convert.ToBase64String(SHA256.HashData(content))}:";

    /// <summary>The message of a chunk whose bytes don't match.</summary>
    public const string Mismatch =
        "The chunk was damaged on the way (its bytes don't match its digest): send it again.";
}
