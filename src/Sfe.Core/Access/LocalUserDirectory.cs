using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Logs;
using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// The accounts with a password in the database. Locks an account for a while after repeated failures, and stores
/// a stronger hash when the password was hashed with fewer iterations than today's.
/// </summary>
public sealed class LocalUserDirectory(
    IDbContextFactory<AccessDbContext> dbFactory,
    AccessChanges changes,
    IOptionsMonitor<SecurityOptions> security,
    ILogger<LocalUserDirectory> logger
) : IUserDirectory
{
    public string Source => UserAccount.LocalSource;

    public async Task<DirectoryResult> ValidateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var name = userName.Trim();
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.UserName == name && u.Source == UserAccount.LocalSource,
            cancellationToken
        );
        if (user is null)
        {
            // As long as a real check, so that the time taken doesn't tell which accounts exist.
            PasswordHashing.Verify(password, DummyHash, out _);
            return new DirectoryResult(DirectoryStatus.Unknown);
        }

        var now = DateTime.UtcNow;
        if (user.LockedUntil > now)
        {
            // Told only to who knows the password: to anyone else a locked account answers as any wrong guess, after
            // the same check, so neither the words nor the time taken tell that the name exists.
            return PasswordHashing.Verify(password, user.PasswordHash, out _)
                ? new DirectoryResult(DirectoryStatus.LockedOut, LockedUntil: user.LockedUntil)
                : new DirectoryResult(DirectoryStatus.Invalid);
        }

        if (!PasswordHashing.Verify(password, user.PasswordHash, out var outdated))
        {
            var options = security.CurrentValue;
            user.FailedSignIns++;
            // From every address together: the Api counts each address's own (SignInThrottle), far fewer.
            if (options.AccountLockoutFailures > 0 && user.FailedSignIns >= options.AccountLockoutFailures)
            {
                user.FailedSignIns = 0;
                user.LockedUntil = now.AddMinutes(Math.Max(1, options.LockoutMinutes));
                logger.LogSecurityWarning(
                    "The account {User} is locked until {Until:u}: {Failures} wrong passwords in a row, "
                        + "from all addresses",
                    user.UserName,
                    user.LockedUntil,
                    options.AccountLockoutFailures
                );
            }

            await db.SaveChangesAsync(cancellationToken);
            changes.Changed();
            return user.LockedUntil > now
                ? new DirectoryResult(DirectoryStatus.LockedOut, LockedUntil: user.LockedUntil)
                : new DirectoryResult(DirectoryStatus.Invalid);
        }

        user.FailedSignIns = 0;
        user.LockedUntil = null;
        if (outdated)
            user.PasswordHash = PasswordHashing.Hash(password);
        await db.SaveChangesAsync(cancellationToken);
        return new DirectoryResult(
            DirectoryStatus.Ok,
            new DirectoryUser(
                Source,
                user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                user.UserName,
                user.DisplayName,
                []
            )
        );
    }

    private static readonly string DummyHash = PasswordHashing.Hash(Guid.NewGuid().ToString());
}
