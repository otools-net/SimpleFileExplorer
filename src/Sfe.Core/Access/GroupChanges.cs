using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// A group made or changed by <see cref="AccessService"/>: its name, directory group, rights and members checked and
/// applied to the tracked row (saved by the caller).
/// </summary>
internal static class GroupChanges
{
    /// <returns>Null once applied; the refusal otherwise (the row may be partly changed: don't save it).</returns>
    public static async Task<AccessResult<UserGroup>?> ApplyAsync(
        AccessDbContext db,
        UserGroup group,
        GroupInput input,
        bool membersOfTenantOnly,
        CancellationToken cancellationToken
    )
    {
        var name = input.Name?.Trim();
        if (await NameErrorAsync(db, group, name, cancellationToken) is { } nameError)
            return nameError;
        if (input.ExternalName is { Length: > 400 })
        {
            return AccessResult<UserGroup>.Invalid(
                "externalName",
                "The sign-in directory's group can have at most 400 characters."
            );
        }

        if (input.Rights is { } rights && ApplyRights(group, rights) is { } rightsError)
            return rightsError;
        if (
            input.MemberIds is { } memberIds
            && await ApplyMembersAsync(db, group, memberIds, membersOfTenantOnly, cancellationToken) is
            { } membersError
        )
        {
            return membersError;
        }

        // Built-in groups keep their name: the pages and the guest rules find them by it.
        if (group.BuiltIn is null)
            group.Name = name!;
        group.ExternalName = string.IsNullOrWhiteSpace(input.ExternalName) ? null : input.ExternalName.Trim();
        return null;
    }

    private static async Task<AccessResult<UserGroup>?> NameErrorAsync(
        AccessDbContext db,
        UserGroup group,
        string? name,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(name) || name.Length > 100)
            return AccessResult<UserGroup>.Invalid("name", "A group needs a name of at most 100 characters.");
        var taken = await db.Groups.AnyAsync(
            g => g.TenantId == group.TenantId && g.Id != group.Id && g.Name == name,
            cancellationToken
        );
        return taken ? AccessResult<UserGroup>.Invalid("name", $"There is already a group named “{name}”.") : null;
    }

    private static AccessResult<UserGroup>? ApplyRights(UserGroup group, IReadOnlyList<string> rights)
    {
        if (rights.FirstOrDefault(r => !RightCatalog.IsTenantRight(r)) is { } unknown)
            return AccessResult<UserGroup>.Invalid("rights", $"“{unknown}” is not a right of a space.");
        if (
            group.BuiltIn == BuiltInGroup.Guests
            && rights.FirstOrDefault(r => RightCatalog.Find(r) is { GuestAllowed: false }) is { } forbidden
        )
        {
            return AccessResult<UserGroup>.Invalid(
                "rights",
                $"Visitors and guest accounts can never have the right “{RightCatalog.Find(forbidden)!.Title}”."
            );
        }

        group.Rights.RemoveAll(r => !rights.Contains(r.Right));
        foreach (var right in rights.Distinct().Where(r => group.Rights.All(existing => existing.Right != r)))
            group.Rights.Add(new GroupRight { Right = right });
        return null;
    }

    private static async Task<AccessResult<UserGroup>?> ApplyMembersAsync(
        AccessDbContext db,
        UserGroup group,
        IReadOnlyList<int> memberIds,
        bool membersOfTenantOnly,
        CancellationToken cancellationToken
    )
    {
        var wanted = memberIds.Distinct().ToList();
        if (await db.Users.CountAsync(u => wanted.Contains(u.Id), cancellationToken) != wanted.Count)
            return AccessResult<UserGroup>.Invalid("memberIds", "An account no longer exists: reload the page.");
        var joining = wanted.Where(u => group.Members.All(m => m.UserId != u)).ToList();
        if (
            membersOfTenantOnly
            && joining.Count > 0
            && await db.Users.AnyAsync(
                u => joining.Contains(u.Id) && !u.Groups.Any(m => m.Group!.TenantId == group.TenantId),
                cancellationToken
            )
        )
        {
            return AccessResult<UserGroup>.Invalid(
                "memberIds",
                "Only accounts of this space can join its groups: a server administrator adds the others."
            );
        }

        group.Members.RemoveAll(m => !wanted.Contains(m.UserId));
        foreach (var userId in joining)
            group.Members.Add(new GroupMember { UserId = userId });
        return null;
    }
}
