using System.Buffers.Text;
using System.Security.Cryptography;
using Sfe.Core.Data;

namespace Sfe.Core.Access;

/// <summary>
/// The form of a personal access token: <c>sfe_&lt;lookup&gt;_&lt;secret&gt;</c>, the lookup 16 random hex digits
/// finding its row, the secret 32 random bytes in base64url of which only the SHA-256 is stored (random bytes need
/// no slow hash: there is nothing to guess from).
/// </summary>
public static class AccessTokenSecret
{
    public const string Scheme = "sfe_";
    private const int SecretBytes = 32;
    private const int ShownSecretCharacters = 4;
    private static readonly int SecretLength = Base64Url.GetEncodedLength(SecretBytes);

    /// <summary>The length of every token.</summary>
    public static readonly int Length = Scheme.Length + AccessToken.LookupLength + 1 + SecretLength;

    /// <summary>A new token: its lookup, the hash of its secret, and the whole token to show once.</summary>
    public static (string Lookup, byte[] Hash, string Token) New()
    {
        var lookup = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(AccessToken.LookupLength / 2));
        var secret = RandomNumberGenerator.GetBytes(SecretBytes);
        var token = $"{Scheme}{lookup}_{Base64Url.EncodeToString(secret)}";
        return (lookup, Hash(secret), token);
    }

    /// <summary>
    /// Splits a presented token into its lookup and its secret's hash; false for anything not in the form.
    /// </summary>
    public static bool TryParse(string? presented, out string lookup, out byte[] hash)
    {
        lookup = string.Empty;
        hash = [];
        if (presented is null || presented.Length != Length || !presented.StartsWith(Scheme, StringComparison.Ordinal))
            return false;

        var lookupPart = presented.AsSpan(Scheme.Length, AccessToken.LookupLength);
        var separator = Scheme.Length + AccessToken.LookupLength;
        if (!IsLowerHex(lookupPart) || presented[separator] != '_')
            return false;

        var secret = new byte[SecretBytes];
        var isSecret =
            Base64Url.TryDecodeFromChars(presented.AsSpan(separator + 1), secret, out var written)
            && written == SecretBytes;
        if (!isSecret)
            return false;

        lookup = lookupPart.ToString();
        hash = Hash(secret);
        return true;
    }

    /// <summary>The first characters of a token, shown in lists: the lookup and a few of the secret.</summary>
    public static string Prefix(string token) =>
        token[..(Scheme.Length + AccessToken.LookupLength + 1 + ShownSecretCharacters)];

    /// <summary>Compares two hashes in a time that doesn't depend on where they differ.</summary>
    public static bool Matches(byte[] presented, byte[] stored) =>
        CryptographicOperations.FixedTimeEquals(presented, stored);

    private static byte[] Hash(byte[] secret) => SHA256.HashData(secret);

    private static bool IsLowerHex(ReadOnlySpan<char> text) => !text.ContainsAnyExcept("0123456789abcdef");
}
