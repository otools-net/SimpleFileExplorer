using Sfe.Core.Data;

namespace Sfe.Core.Access;

/// <param name="GuestAllowed">
/// Guests (visitors without an account and guest accounts) may get it. Off for everything that changes the library,
/// the settings or the accounts, and for what tells about the server: such a right is never theirs, whatever their
/// groups grant.
/// </param>
/// <param name="DefaultGroups">The built-in groups granted it when a space is created; changed by hand
/// afterwards.</param>
/// <param name="Area">Heading the right is listed under on the groups page.</param>
/// <param name="Since">
/// The catalog revision that added it (<see cref="RightCatalog.Revision"/>): the spaces created before get it once, in
/// their built-in groups by <paramref name="DefaultGroups"/> (<see cref="AccessService.GrantNewRightsAsync"/>).
/// </param>
/// <param name="Kind">Write when any route it opens changes something (files, index, tags, settings, accounts).</param>
/// <param name="AtFolder">
/// What a folder must allow for the right to apply in it: <see cref="FolderLevel.Write"/> only for rights that change
/// the library's files on disk; null for a right of the whole space (settings, tasks, accounts).
/// </param>
public sealed record RightDefinition(
    string Id,
    RightScope Scope,
    bool GuestAllowed,
    IReadOnlyList<BuiltInGroup> DefaultGroups,
    string Area,
    string Title,
    string Description,
    int Since = 0,
    RightKind Kind = RightKind.Read,
    FolderLevel? AtFolder = null
)
{
    /// <summary>Changes the library's files on disk (selector <c>folder:write</c>).</summary>
    public bool ChangesFiles => AtFolder == FolderLevel.Write;
}
