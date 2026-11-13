using Sfe.Core.Connections;

namespace Sfe.Tests.Infrastructure;

/// <summary>
/// Protects by a visible prefix, so a test sees what is stored is not the credentials themselves; text without the
/// prefix reads as protected with other keys (null).
/// </summary>
public sealed class FakeProtector : ICredentialProtector
{
    public const string Prefix = "protected:";

    public string Protect(string credentials) => Prefix + credentials;

    public string? Unprotect(string protectedCredentials) =>
        protectedCredentials.StartsWith(Prefix, StringComparison.Ordinal)
            ? protectedCredentials[Prefix.Length..]
            : null;
}
