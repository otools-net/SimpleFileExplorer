using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Options;

namespace Sfe.Core.Access;

/// <summary>
/// Where accounts and passwords are checked. The local accounts are one (<see cref="LocalUserDirectory"/>); a
/// directory such as LDAP is another, registered next to it and named in <see cref="SecurityOptions.Directories"/>.
/// Whatever answers, rights, groups and preferences hang on the account's row in <see cref="AccessDbContext.Users"/>:
/// a directory account gets one on its first sign-in (<see cref="SignInService"/>).
/// </summary>
public interface IUserDirectory
{
    /// <summary>Name in <see cref="UserAccount.Source"/> and in <see cref="SecurityOptions.Directories"/>.</summary>
    string Source { get; }

    Task<DirectoryResult> ValidateAsync(string userName, string password, CancellationToken cancellationToken);
}

/// <param name="ExternalId">The directory's own id of the account (for local accounts, the row's id).</param>
/// <param name="Groups">The directory groups it belongs to, matched to <see cref="UserGroup.ExternalName"/>.</param>
public sealed record DirectoryUser(string Source, string ExternalId, string UserName, string? DisplayName, IReadOnlyList<string> Groups);

public enum DirectoryStatus
{
    Ok,

    /// <summary>The account is not in this directory: ask the next one.</summary>
    Unknown,

    /// <summary>The account is there but the password is wrong.</summary>
    Invalid,

    /// <summary>Too many failed sign-ins: refused until <see cref="DirectoryResult.LockedUntil"/>.</summary>
    LockedOut,
}

public sealed record DirectoryResult(DirectoryStatus Status, DirectoryUser? User = null, DateTime? LockedUntil = null);

/// <summary>
/// The accounts with a password in the database. Locks an account for a while after repeated failures, and stores
/// a stronger hash when the password was hashed with fewer iterations than today's.
/// </summary>
public sealed class LocalUserDirectory(IDbContextFactory<AccessDbContext> dbFactory, AccessChanges changes, IOptionsMonitor<SecurityOptions> security) : IUserDirectory
{
    public string Source => UserAccount.LocalSource;

    public async Task<DirectoryResult> ValidateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var name = userName.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == name && u.Source == UserAccount.LocalSource, cancellationToken);
        if (user is null)
        {
            // As long as a real check, so that the time taken doesn't tell which accounts exist.
            PasswordHashing.Verify(password, DummyHash, out _);
            return new DirectoryResult(DirectoryStatus.Unknown);
        }

        var now = DateTime.UtcNow;
        if (user.LockedUntil > now)
            return new DirectoryResult(DirectoryStatus.LockedOut, LockedUntil: user.LockedUntil);

        if (!PasswordHashing.Verify(password, user.PasswordHash, out var outdated))
        {
            var options = security.CurrentValue;
            user.FailedSignIns++;
            if (options.MaxFailedSignIns > 0 && user.FailedSignIns >= options.MaxFailedSignIns)
            {
                user.FailedSignIns = 0;
                user.LockedUntil = now.AddMinutes(Math.Max(1, options.LockoutMinutes));
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
        return new DirectoryResult(DirectoryStatus.Ok, new DirectoryUser(Source, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), user.UserName, user.DisplayName, []));
    }

    private static readonly string DummyHash = PasswordHashing.Hash(Guid.NewGuid().ToString());
}
