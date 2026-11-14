namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// Protects what connections store to sign in again (refresh tokens) with the server's own keys, so the database
/// and its backups hold nothing usable without the keys folder. Implemented by the Api with Data Protection
/// (purpose <c>SFE.Connections</c>).
/// </summary>
public interface ICredentialProtector
{
    string Protect(string credentials);

    /// <summary>
    /// Null when the text cannot be read back (other keys: a backup restored without its keys folder).
    /// </summary>
    string? Unprotect(string protectedCredentials);
}
