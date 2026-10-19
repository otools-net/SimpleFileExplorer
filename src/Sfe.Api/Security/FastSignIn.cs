using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Sfe.Core.Access;
using Sfe.Core.Settings;

namespace Sfe.Api.Security;

/// <summary>
/// The one-time link that signs the server's only administrator in, in the browser SFE opens when it starts
/// (<see cref="Sfe.Core.Options.SfeOptions.FastSignIn"/>): for an app used on this computer alone. One link at a time,
/// kept in memory only (as a hash), for <see cref="Lifetime"/>; it works once, from this computer only
/// (<see cref="LocalVisitor.IsOnThisComputer"/>), and only while that account is still the only enabled administrator
/// and the server is Local (<see cref="ServerModes"/>): an External server is used by others.
/// </summary>
public sealed class FastSignIn(AccessService accounts, SettingLayers settings, TimeProvider time, IConfiguration configuration, ILogger<FastSignIn> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    /// <summary>The route that takes the link (<c>?token=</c>), relative to the app's address.</summary>
    public const string Route = "api/session/start";

    private readonly Lock _lock = new();
    private (byte[] Hash, int UserId, DateTimeOffset Expires)? _pending;

    /// <summary>
    /// A new link's token for the only enabled server administrator; null when fast sign-in is off or there isn't
    /// exactly one. Replaces any link not used yet.
    /// </summary>
    public async Task<string?> IssueAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.ServerLevel().FastSignIn)
            return null;
        if (!ServerModes.IsLocal(configuration))
        {
            logger.LogInformation("Fast sign-in skipped: SFE is in External mode");
            return null;
        }

        var admins = await accounts.GetServerAdminIdsAsync(cancellationToken);
        if (admins.Count != 1)
        {
            logger.LogInformation("Fast sign-in skipped: it needs a single enabled server administrator, there are {Count}", admins.Count == 0 ? "none" : "several");
            return null;
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        lock (_lock)
            _pending = (Hash(token), admins[0], time.GetUtcNow() + Lifetime);
        return token;
    }

    /// <summary>The account <paramref name="token"/> signs in, once; null when it is not the pending link (or no longer valid).</summary>
    public async Task<SessionUser?> RedeemAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
            return null;
        int userId;
        lock (_lock)
        {
            if (_pending is not { } pending || !CryptographicOperations.FixedTimeEquals(pending.Hash, Hash(token)))
                return null;
            _pending = null;
            if (time.GetUtcNow() > pending.Expires)
                return null;
            userId = pending.UserId;
        }

        // Still on, still Local, and still the only administrator: the setting, the mode or the accounts may have changed since the start.
        if (!settings.ServerLevel().FastSignIn || !ServerModes.IsLocal(configuration) || await accounts.GetServerAdminIdsAsync(cancellationToken) is not [var only] || only != userId)
            return null;
        return await accounts.GetSessionUserAsync(userId, cancellationToken);
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
