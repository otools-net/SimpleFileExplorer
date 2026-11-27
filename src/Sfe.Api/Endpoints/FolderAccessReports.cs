using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// Who may see and change a folder of a space (<c>GET api/access/folder-access</c>): every group by its level there and
/// its rights, every account with all its groups and rules counted, and visitors when the space lets them in.
/// </summary>
internal sealed class FolderAccessReports(
    FolderRuleService rules,
    AccessService accounts,
    RightsEvaluator evaluator,
    ManagedSpace space
)
{
    /// <summary>The report of <paramref name="folder"/> (root-relative), whose full path is <paramref name="full"/>.</summary>
    public async Task<FolderAccessReport> BuildAsync(string folder, string full, CancellationToken cancellationToken)
    {
        var all = await rules.GetAsync(space.Id, cancellationToken);
        var groups = await accounts.GetGroupsAsync(space.Id, cancellationToken);
        var users = await accounts.GetUsersAsync(space.Id, cancellationToken);
        var info = await accounts.GetTenantAsync(space.Id, cancellationToken);
        var at = new RulesAt(all, folder);

        var groupEntries = groups.Select(g => GroupEntry(g, at)).ToList();
        var accountEntries = new List<FolderAccessEntry>();
        foreach (var user in users.OrderBy(u => u.DisplayName ?? u.UserName, StringComparer.CurrentCultureIgnoreCase))
            accountEntries.Add(await AccountEntryAsync(user, at, cancellationToken));

        FolderAccessEntry? visitors = null;
        if (
            info is { GuestAccess: true }
            && groups.FirstOrDefault(g => g.BuiltIn == Core.Data.BuiltInGroup.Guests) is { } guests
        )
        {
            var (sees, changes) = await EffectiveAsync(null, folder, cancellationToken);
            visitors = new FolderAccessEntry(
                guests.Id,
                null,
                "Visitors without an account",
                groupEntries.First(e => e.GroupId == guests.Id).Level,
                false,
                sees,
                changes
            );
        }

        return new FolderAccessReport(folder, Directory.Exists(full), groupEntries, accountEntries, visitors);
    }

    private static FolderAccessEntry GroupEntry(GroupInfo group, RulesAt at)
    {
        var principal = FolderPrincipalRef.Group(group.Id);
        var level = FolderPolicy.LevelAt(at.Of(principal), at.Folder, true);
        var granted = group.Rights.ToHashSet();
        return new FolderAccessEntry(
            group.Id,
            null,
            group.Name,
            level,
            at.IsExplicit(principal),
            level >= FolderLevel.Read && granted.Contains(Rights.Browse),
            level == FolderLevel.Write && RightCatalog.All.Any(r => r.ChangesFiles && granted.Contains(r.Id))
        );
    }

    private async Task<FolderAccessEntry> AccountEntryAsync(
        UserInfo user,
        RulesAt at,
        CancellationToken cancellationToken
    )
    {
        var principal = FolderPrincipalRef.Account(user.Id);
        var (sees, changes) = await EffectiveAsync(user.Id, at.Folder, cancellationToken);
        return new FolderAccessEntry(
            null,
            user.Id,
            user.DisplayName ?? user.UserName,
            FolderPolicy.LevelAt(at.Of(principal), at.Folder, false),
            at.IsExplicit(principal),
            sees,
            changes
        );
    }

    /// <summary>What an account (null: a visitor) may do at the folder, all its groups and rules counted.</summary>
    private async Task<(bool Sees, bool ChangesFiles)> EffectiveAsync(
        int? userId,
        string folder,
        CancellationToken cancellationToken
    )
    {
        var access = (await evaluator.GetAsync(new AccessSubject(space.Id, userId), cancellationToken)).At(folder);
        return (access.Has(Rights.Browse), RightCatalog.All.Any(r => r.ChangesFiles && access.Has(r.Id)));
    }

    /// <summary>The space's rules, read for one folder.</summary>
    private sealed record RulesAt(IReadOnlyList<FolderRuleInfo> All, string Folder)
    {
        /// <summary>The rules of one principal, nearest-wins map.</summary>
        public Dictionary<string, FolderLevel> Of(FolderPrincipalRef principal) =>
            RightsEvaluator.RuleMap(All.Where(r => r.Principal == principal).Select(r => (r.Path, r.Level)));

        /// <summary>The principal has a rule on this very folder.</summary>
        public bool IsExplicit(FolderPrincipalRef principal) =>
            All.Any(r => r.Principal == principal && LibraryPaths.PathComparer.Equals(r.Path, Folder));
    }
}
