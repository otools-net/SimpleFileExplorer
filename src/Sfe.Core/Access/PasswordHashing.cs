using System.Security.Cryptography;
using System.Text;

namespace Sfe.Core.Access;

/// <summary>
/// Passwords of local accounts: PBKDF2 with SHA-512 and a random salt, stored as
/// <c>pbkdf2-sha512$iterations$salt$hash</c> (Base64). The iteration count travels with the hash, so raising it later
/// still verifies older hashes, which are then replaced (<see cref="Verify"/>'s <c>outdated</c>).
/// </summary>
public static class PasswordHashing
{
    /// <summary>OWASP's figure for PBKDF2-HMAC-SHA512 (2023).</summary>
    public const int Iterations = 210_000;

    private const string Prefix = "pbkdf2-sha512";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA512, HashBytes);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <param name="outdated">The hash is right but made with fewer iterations than today's: store a new one.</param>
    public static bool Verify(string password, string? stored, out bool outdated)
    {
        outdated = false;
        var parts = stored?.Split('$');
        if (parts is not [Prefix, var count, var saltText, var hashText] || !int.TryParse(count, out var iterations) || iterations <= 0)
            return false;

        try
        {
            var salt = Convert.FromBase64String(saltText);
            var expected = Convert.FromBase64String(hashText);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, expected.Length);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                return false;
        }
        catch (FormatException)
        {
            return false;
        }

        outdated = iterations < Iterations;
        return true;
    }

    /// <summary>A new random value for <see cref="Data.UserAccount.SecurityStamp"/>.</summary>
    public static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Compares two secrets in a time that doesn't depend on where they differ.</summary>
    public static bool SameSecret(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
