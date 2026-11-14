using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// The credentials of connections protected with the server's Data Protection keys (the keys folder,
/// <see cref="SecurityServiceCollectionExtensions"/>), under their own purpose: a backup of the database alone holds
/// nothing usable.
/// </summary>
public sealed class ConnectionCredentials(IDataProtectionProvider provider) : ICredentialProtector
{
    public const string Purpose = "SFE.Connections";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string credentials) => _protector.Protect(credentials);

    public string? Unprotect(string protectedCredentials)
    {
        try
        {
            return _protector.Unprotect(protectedCredentials);
        }
        catch (CryptographicException)
        {
            // Other keys (a backup restored without its keys folder), or not protected text at all.
            return null;
        }
    }
}
