using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <param name="Url">
/// Relative to the app (<c>api/files/flash/{token}/{name}</c>): the movie, whose relative addresses (<c>level2.swf</c>)
/// resolve under the token, to the files of its folder.
/// </param>
public sealed record FlashLink(string Url, DateTimeOffset Expires);

/// <summary>
/// Links for the Flash viewer (agents/docs/features/flash-viewer.md). Ruffle plays a movie in a frame of no origin, so
/// that the movie can never send the session's cookie: what it loads comes through a link whose token stands for the
/// sign-in instead, read only, limited to the movie's folder (and its subfolders), and checked again on every request
/// against the account and its rights as they are then. <see cref="FileLinkClaims.Path"/> is that folder.
/// </summary>
public sealed class FlashLinks(IDataProtectionProvider protection)
{
    /// <summary>How long a movie may keep loading its files (levels, music...) after it was opened.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    // A purpose of its own: an external player's link (one file) can't stand for a folder here, nor the reverse.
    private readonly ITimeLimitedDataProtector _protector = protection.CreateProtector("Sfe.FlashLinks.v1").ToTimeLimitedDataProtector();

    /// <summary>A link to the movie at <paramref name="path"/>, opening its folder to the movie.</summary>
    public FlashLink Create(int tenantId, SessionUser? user, string path)
    {
        var slash = path.LastIndexOf('/');
        var claims = new FileLinkClaims(tenantId, user?.Id, user?.SecurityStamp, slash < 0 ? string.Empty : path[..slash]);
        var token = _protector.Protect(JsonSerializer.Serialize(claims), Lifetime);
        return new FlashLink($"api/files/flash/{token}/{Uri.EscapeDataString(path[(slash + 1)..])}", DateTimeOffset.UtcNow + Lifetime);
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
