using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>What keeps the folder rules of a connection meaningful (<see cref="ConnectionRuleService"/>).</summary>
internal static class ConnectionRuleChecks
{
    /// <summary>
    /// Why the rules can't be set, without the database; null when they can, with their paths normalised.
    /// </summary>
    public static (string Key, string Message)? InputError(
        IReadOnlyList<ConnectionRuleInput> rules,
        out List<ConnectionRuleInput> normalized
    )
    {
        normalized = [];
        if (rules.Count > ConnectionRuleService.MaxRules)
            return ("rules", $"A connection can have at most {ConnectionRuleService.MaxRules} folder rules here.");
        foreach (var rule in rules)
        {
            if (RuleError(rule) is { } error)
                return error;
            normalized.Add(rule with { Path = RemotePaths.Normalize(rule.Path) });
        }
        return null;
    }

    /// <summary>Two rules for one principal at one path, as the provider compares paths.</summary>
    public static bool HasTwins(IReadOnlyList<ConnectionRuleInput> rules, StringComparer paths) =>
        rules
            .GroupBy(r => (r.GroupId, r.UserId))
            .Any(principal => principal.Select(r => r.Path!).Distinct(paths).Count() != principal.Count());

    /// <summary>Groups of library <paramref name="tenantId"/> only, accounts that are members of it.</summary>
    public static async Task<(string Key, string Message)?> PrincipalErrorAsync(
        AccessDbContext db,
        int tenantId,
        IReadOnlyList<ConnectionRuleInput> rules,
        CancellationToken cancellationToken
    )
    {
        var groupIds = rules.Where(r => r.GroupId is not null).Select(r => r.GroupId!.Value).Distinct().ToList();
        var groups = await db.Groups.CountAsync(
            g => g.TenantId == tenantId && groupIds.Contains(g.Id),
            cancellationToken
        );
        if (groups != groupIds.Count)
            return ("groupId", "Not a group of this space.");

        var userIds = rules.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var members = await db
            .GroupMembers.Where(m => userIds.Contains(m.UserId) && m.Group!.TenantId == tenantId)
            .Select(m => m.UserId)
            .Distinct()
            .CountAsync(cancellationToken);
        return members == userIds.Count ? null : ("userId", "The account isn't in this space.");
    }

    private static (string Key, string Message)? RuleError(ConnectionRuleInput rule)
    {
        if ((rule.GroupId is null) == (rule.UserId is null))
            return ("principal", "Name a group or an account.");
        if (!Enum.IsDefined(rule.Level))
            return ("level", "Unknown level.");
        // Null too for a path longer than what is stored.
        if (RemotePaths.Normalize(rule.Path) is null)
            return ("path", "Not a folder path.");
        if (rule.ItemId?.Length > ConnectionFolderRule.MaxItemIdLength)
            return ("itemId", $"The item id can have at most {ConnectionFolderRule.MaxItemIdLength} characters.");
        return null;
    }
}
