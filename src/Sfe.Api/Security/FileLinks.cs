using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <summary>
/// Links for external players (VLC, Infuse...), which fetch the file themselves without the browser's cookie: the
/// link carries a token instead, valid for a while (<c>Player:LinkLifetimeMinutes</c>). The token is checked again on
/// every request (players ask for ranges as they seek), against the account and its rights as they are then.
/// </summary>
public sealed class FileLinks(IDataProtectionProvider protection)
{
    private readonly ITimeLimitedDataProtector _protector = protection
        .CreateProtector("Sfe.FileLinks.v1")
        .ToTimeLimitedDataProtector();

    public FileLink Create(int tenantId, SessionUser? user, string path, TimeSpan lifetime)
    {
        var claims = new FileLinkClaims(tenantId, user?.Id, user?.SecurityStamp, path);
        var token = _protector.Protect(JsonSerializer.Serialize(claims), lifetime);
        var name = path[(path.LastIndexOf('/') + 1)..];
        return new FileLink($"api/files/shared/{token}/{Uri.EscapeDataString(name)}", DateTimeOffset.UtcNow + lifetime);
    }

    /// <summary>The claims of a token that is genuine and not expired; null otherwise.</summary>
    public FileLinkClaims? Read(string token)
    {
        try
        {
            return JsonSerializer.Deserialize<FileLinkClaims>(_protector.Unprotect(token, out _));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
