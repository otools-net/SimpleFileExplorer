using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <param name="Url">Relative to the app (<c>api/files/shared/{token}/{name}</c>); it ends with the file's name, which players read the format from.</param>
public sealed record FileLink(string Url, DateTimeOffset Expires);

/// <summary>What a link's token holds: signed and encrypted, it cannot be read, changed or widened to another file.</summary>
/// <param name="User">The account that asked for the link; null for a visitor without one (checked against the guests' rights).</param>
/// <param name="Stamp">The account's security stamp then: a new password or a disabled account ends every link it gave out.</param>
public sealed record FileLinkClaims(
    [property: JsonPropertyName("t")] int Tenant,
    [property: JsonPropertyName("u")] int? User,
    [property: JsonPropertyName("s")] string? Stamp,
    [property: JsonPropertyName("p")] string Path);

/// <summary>
/// Links for external players (VLC, Infuse...), which fetch the file themselves without the browser's cookie: the
/// link carries a token instead, valid for a while (<c>Player:LinkLifetimeMinutes</c>). The token is checked again on
/// every request (players ask for ranges as they seek), against the account and its rights as they are then.
/// </summary>
public sealed class FileLinks(IDataProtectionProvider protection)
{
    private readonly ITimeLimitedDataProtector _protector = protection.CreateProtector("Sfe.FileLinks.v1").ToTimeLimitedDataProtector();

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
