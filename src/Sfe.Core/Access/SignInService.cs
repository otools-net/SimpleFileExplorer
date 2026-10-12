using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Options;

namespace Sfe.Core.Access;

public enum SignInStatus
{
    Ok,
    Invalid,
    LockedOut,
    Disabled,
}

public sealed record SignInResult(SignInStatus Status, SessionUser? User = null, DateTime? LockedUntil = null);

/// <summary>
/// Checks a name and password against the directories in their configured order (the local accounts when none is
/// named): the first one that knows the account decides. A directory account gets a local row on its first
/// sign-in, and its groups mapped by <see cref="UserGroup.ExternalName"/> follow the directory's at each sign-in.
/// </summary>
public sealed class SignInService(
    IEnumerable<IUserDirectory> directories,
    IDbContextFactory<AccessDbContext> dbFactory,
    AccessService accounts,
    AccessChanges changes,
    IOptionsMonitor<SecurityOptions> security,
    ILogger<SignInService> logger)
{
    public async Task<SignInResult> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return new SignInResult(SignInStatus.Invalid);

        foreach (var directory in OrderedDirectories())
        {
            var result = await directory.ValidateAsync(userName, password, cancellationToken);
            switch (result.Status)
            {
                case DirectoryStatus.Unknown:
                    continue;
                case DirectoryStatus.Invalid:
                    return new SignInResult(SignInStatus.Invalid);
                case DirectoryStatus.LockedOut:
                    return new SignInResult(SignInStatus.LockedOut, LockedUntil: result.LockedUntil);
            }

            var userId = result.User!.Source == UserAccount.LocalSource
                ? int.Parse(result.User.ExternalId, CultureInfo.InvariantCulture)
                : await ProvisionAsync(result.User, cancellationToken);
            if (userId is null)
                return new SignInResult(SignInStatus.Invalid);

            var user = await accounts.GetSessionUserAsync(userId.Value, cancellationToken);
            if (user is null)
                return new SignInResult(SignInStatus.Invalid);
            if (user.Disabled)
                return new SignInResult(SignInStatus.Disabled);

            await accounts.RecordSignInAsync(user.Id, cancellationToken);
            return new SignInResult(SignInStatus.Ok, user);
        }

        return new SignInResult(SignInStatus.Invalid);
    }

    private IEnumerable<IUserDirectory> OrderedDirectories()
    {
        var names = security.CurrentValue.Directories;
        var all = directories.ToList();
        if (names.Count == 0)
            return all.Where(d => d.Source == UserAccount.LocalSource);

        foreach (var missing in names.Where(n => all.All(d => !string.Equals(d.Source, n, StringComparison.OrdinalIgnoreCase))))
            logger.LogWarning("Sfe:Security:Directories names {Directory}, which this server doesn't have", missing);
        return names
            .Select(n => all.FirstOrDefault(d => string.Equals(d.Source, n, StringComparison.OrdinalIgnoreCase)))
            .OfType<IUserDirectory>();
    }

    /// <summary>
    /// The local row of a directory account (found by the directory's id, created on the first sign-in) with its
    /// mapped groups brought in line with the directory; null when its name is taken by another account.
    /// </summary>
    private async Task<int?> ProvisionAsync(DirectoryUser external, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.Include(u => u.Groups).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.Source == external.Source && u.ExternalId == external.ExternalId, cancellationToken);
        if (user is null)
        {
            if (await db.Users.AnyAsync(u => u.UserName == external.UserName, cancellationToken))
            {
                logger.LogWarning("The {Directory} account {User} cannot sign in: another account has that name", external.Source, external.UserName);
                return null;
            }

            user = new UserAccount
            {
                UserName = external.UserName,
                Source = external.Source,
                ExternalId = external.ExternalId,
                SecurityStamp = PasswordHashing.NewStamp(),
                Created = DateTime.UtcNow,
            };
            db.Users.Add(user);
        }

        user.DisplayName = external.DisplayName ?? user.DisplayName;
        var mapped = await db.Groups.Where(g => g.ExternalName != null).ToListAsync(cancellationToken);
        var wanted = mapped.Where(g => external.Groups.Contains(g.ExternalName!, StringComparer.OrdinalIgnoreCase)).Select(g => g.Id).ToHashSet();
        user.Groups.RemoveAll(m => m.Group?.ExternalName is not null && !wanted.Contains(m.GroupId));
        foreach (var groupId in wanted.Where(id => user.Groups.All(m => m.GroupId != id)))
            user.Groups.Add(new GroupMember { GroupId = groupId });

        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
        return user.Id;
    }
}
