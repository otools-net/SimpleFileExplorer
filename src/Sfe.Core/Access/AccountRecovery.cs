using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Data;
using Sfe.Core.Logs;

namespace Sfe.Core.Access;

/// <param name="UserName">The account the code recovers.</param>
/// <param name="Code">Shown only where the server runs (its console and <see cref="AccountRecovery.FilePath"/>), never to the browser.</param>
public sealed record RecoveryCode(int UserId, string UserName, string Code, DateTime Expires);

/// <summary>
/// The way back in for a server administrator locked out (by someone guessing the password) or who forgot the
/// password, without editing a file or restarting: the sign-in page asks for a one-time code, which the server shows
/// only where it runs (its console, and <see cref="FilePath"/> for a service without one); with it, a new password
/// unlocks the account and signs it in. Whoever reads the console runs the server, which is what the code proves. A
/// code lives <see cref="Lifetime"/>, works once, and exists only while someone recovers: no lasting secret, unlike the
/// setup code. Other accounts are unlocked by an administrator; a disabled administrator needs <c>Sfe:Setup:Recover</c>.
/// </summary>
public sealed class AccountRecovery(IDbContextFactory<AccessDbContext> dbFactory, AccessService accounts, string filePath, ILogger<AccountRecovery> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<int, RecoveryCode> _codes = new();
    private readonly Lock _fileLock = new();

    /// <summary>Where the pending codes are also written, for a server without a console (a Windows service).</summary>
    public string FilePath { get; } = filePath;

    /// <summary>
    /// The pending code of <paramref name="userName"/>, made when it has none: the same one until it expires, so that
    /// others asking again can't replace the code its administrator is reading. Null when the name is not an enabled
    /// local server administrator (the caller answers the same either way).
    /// </summary>
    /// <returns>The code, and whether it was just made.</returns>
    public async Task<(RecoveryCode Code, bool New)?> RequestAsync(string? userName, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(userName, cancellationToken) is not { } user)
            return null;

        var now = DateTime.UtcNow;
        if (_codes.TryGetValue(user.Id, out var pending) && pending.Expires > now)
            return (pending, false);

        var code = new RecoveryCode(user.Id, user.UserName, GeneratedSetupCode.Generate(), now + Lifetime);
        _codes[user.Id] = code;
        WriteFile();
        return (code, true);
    }

    /// <summary>
    /// Gives <paramref name="userName"/> the password <paramref name="password"/> with its pending code: unlocks it and
    /// ends its other sessions (a new security stamp). The code is used up only when it works.
    /// </summary>
    public async Task<AccessResult<SessionUser>> CompleteAsync(string? userName, string? code, string? password, CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(userName, cancellationToken);
        if (user is null || !_codes.TryGetValue(user.Id, out var pending) || pending.Expires <= DateTime.UtcNow
            || !PasswordHashing.SameSecret(GeneratedSetupCode.Normalize(code), GeneratedSetupCode.Normalize(pending.Code)))
        {
            logger.LogSecurityWarning("Account recovery refused for {User}: wrong or expired code", userName);
            return AccessResult<SessionUser>.Invalid("code", "This recovery code is wrong or has expired: ask for a new one.");
        }

        var result = await accounts.UpdateUserAsync(user.Id,
            new UserInput(user.UserName, user.DisplayName, password ?? string.Empty, user.Kind, IsServerAdmin: true, Disabled: false, Unlock: true),
            null, cancellationToken);
        if (result.Status != AccessStatus.Ok)
            return result.As<SessionUser>();

        _codes.TryRemove(user.Id, out _);
        WriteFile();
        logger.LogSecurityWarning("Account recovery: {User} has a new password and is unlocked; its other sessions ended", user.UserName);
        return AccessResult<SessionUser>.Ok((await accounts.GetSessionUserAsync(user.Id, cancellationToken))!);
    }

    private async Task<UserAccount?> FindAsync(string? userName, CancellationToken cancellationToken)
    {
        var name = userName?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserName == name && u.Source == UserAccount.LocalSource && u.IsServerAdmin && !u.Disabled, cancellationToken);
    }

    /// <summary>The pending codes, one per line; no file when there is none.</summary>
    private void WriteFile()
    {
        lock (_fileLock)
        {
            var now = DateTime.UtcNow;
            var lines = _codes.Values.Where(c => c.Expires > now).OrderBy(c => c.UserName, StringComparer.Ordinal)
                .Select(c => $"{c.UserName}: {c.Code} (until {c.Expires:HH:mm} UTC)").ToList();
            try
            {
                if (lines.Count == 0)
                {
                    File.Delete(FilePath);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllLines(FilePath, ["Codes to recover an administrator's account on the sign-in page, each usable once:", .. lines]);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Cannot write the recovery codes to {Path}: they are only in the console", FilePath);
            }
        }
    }
}
