using Microsoft.AspNetCore.DataProtection;
using Sfe.Api.Security;

namespace Sfe.Tests;

/// <summary>The credentials of connections protected with the server's Data Protection keys.</summary>
[TestClass]
public sealed class ConnectionCredentialsTests
{
    [TestMethod]
    public void CredentialsAreReadBackWithTheSameKeysOnly()
    {
        var keys = new EphemeralDataProtectionProvider();
        var credentials = new ConnectionCredentials(keys);

        var stored = credentials.Protect("refresh-token");

        Assert.DoesNotContain("refresh-token", stored);
        Assert.AreEqual("refresh-token", credentials.Unprotect(stored));
        Assert.IsNull(new ConnectionCredentials(new EphemeralDataProtectionProvider()).Unprotect(stored));
        Assert.IsNull(credentials.Unprotect("refresh-token"));
        // Another purpose with the same keys does not read them either.
        Assert.ThrowsExactly<System.Security.Cryptography.CryptographicException>(() =>
            keys.CreateProtector("SFE.Other").Unprotect(stored)
        );
    }
}
