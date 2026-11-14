using Sfe.Core.Access;
using Sfe.Core.Library;

namespace Sfe.Core.Connections;

/// <summary>
/// What a subject may do at each path of one connection: its <see cref="ConnectionPolicies"/> policy with the rights
/// it holds in the library of the request (<paramref name="granted"/>, the server's and library's switches already
/// applied). Paths are normalised (<see cref="RemotePaths.Normalize"/>) and compared by the provider's rule for case.
/// </summary>
/// <param name="rulePlaces">The paths of the subject's rules by the provider's id of their folder: an item with that
/// id elsewhere (renamed or moved outside SFE) also gets what the rule's place allows.</param>
public sealed class ConnectionFolders(
    FolderPolicy policy,
    IReadOnlySet<string> granted,
    StringComparison comparison,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? rulePlaces = null
)
{
    /// <summary>No folder rule nor level narrows the rights: an owner, a server administrator.</summary>
    public bool IsUnrestricted => policy.IsUnrestricted;

    /// <summary>How the provider compares names.</summary>
    public StringComparison Comparison => comparison;

    /// <summary>What the subject may do at <paramref name="path"/>.</summary>
    public FolderAccess At(string path) => policy.At(path, granted);

    /// <summary>
    /// What the subject may do at the item at <paramref name="path"/> whose provider id is <paramref name="itemId"/>:
    /// where a rule was set on that item at another path (it was renamed or moved outside SFE), also what is allowed
    /// at the rule's place; the stricter wins. Only where the item's id is at hand (listings, items read).
    /// </summary>
    public FolderAccess At(string path, string? itemId)
    {
        var access = At(path);
        if (itemId is null || rulePlaces is null || !rulePlaces.TryGetValue(itemId, out var places))
            return access;
        foreach (var place in places.Where(place => !place.Equals(path, comparison)))
            access = access.And(At(place));
        return access;
    }

    /// <summary>
    /// The subject sees <paramref name="item"/>, by its path and its id (<see cref="At(string, string?)"/>).
    /// </summary>
    public bool Sees(RemoteItem item) => At(item.Path, item.Id).Has(Rights.Browse);

    /// <summary>The subject may use <paramref name="right"/> at <paramref name="path"/>.</summary>
    public bool Allows(string path, string right) => At(path).Has(right);

    /// <summary>The subject sees the item at <paramref name="path"/> itself (not only passes through it).</summary>
    public bool Sees(string path) => Allows(path, Rights.Browse);

    /// <summary>
    /// The subject may use <paramref name="right"/> at <paramref name="path"/> and everywhere below it: the check
    /// before changing or copying a whole tree, so that nobody reaches what is hidden from them.
    /// </summary>
    public bool AllowsThroughout(string path, string right) => policy.AllowsThroughout(path, right, granted);

    /// <summary>The subject may use <paramref name="right"/> somewhere in the connection.</summary>
    public bool AllowsSomewhere(string right) => policy.Paths.Any(path => Allows(path, right));

    /// <summary>
    /// What a listing of <paramref name="folder"/> leaves out: the children the subject doesn't see, or all but those
    /// leading to what it sees when the folder only leads there; null when nothing is left out.
    /// </summary>
    public ListingFilter? ListingFilterOf(string folder, FolderAccess access)
    {
        if (policy.IsUnrestricted)
            return null;
        var comparer = StringComparer.FromComparison(comparison);
        var children = policy.ChildrenWithRules(folder, granted);
        var hidden = children.Where(c => !c.Value.IsVisible).Select(c => c.Key).ToHashSet(comparer);
        if (!access.PathOnly)
            return hidden.Count == 0 ? null : new ListingFilter(hidden);
        var leading = children.Where(c => c.Value.IsVisible).Select(c => c.Key).ToHashSet(comparer);
        return new ListingFilter(hidden, leading);
    }

    /// <summary>
    /// What the subject may do in <paramref name="folder"/> and in its children that differ, for the client (as
    /// the library's listing tells it); null when unrestricted.
    /// </summary>
    public FolderAccessView? ViewOf(string folder, FolderAccess access)
    {
        if (policy.IsUnrestricted)
            return null;
        var children = policy
            .ChildrenWithRules(folder, granted)
            .Where(c => c.Value.IsVisible)
            .ToDictionary(c => c.Key, c => View(c.Value, null), StringComparer.FromComparison(comparison));
        return View(access, children);
    }

    /// <summary>
    /// <see cref="ListingFilterOf"/> for a page read: <paramref name="listing"/> without the items a rule set on them
    /// at another path hides (found by their id), its counts lowered by them.
    /// </summary>
    public RemoteListing WithoutHiddenByIds(RemoteListing listing)
    {
        if (rulePlaces is not { Count: > 0 })
            return listing;
        var hidden = listing.Items.Where(item => !At(item.Path, item.Id).IsVisible).ToList();
        if (hidden.Count == 0)
            return listing;
        return listing with
        {
            Items = [.. listing.Items.Except(hidden)],
            Total = listing.Total - hidden.Count,
            DirectoryCount = listing.DirectoryCount - hidden.Count(item => item.IsFolder),
        };
    }

    /// <summary>
    /// <see cref="ViewOf(string, FolderAccess)"/> with the children of <paramref name="listed"/> a rule set on them
    /// at another path applies to (found by their id).
    /// </summary>
    public FolderAccessView? ViewOf(string folder, FolderAccess access, IReadOnlyList<RemoteItem> listed)
    {
        var view = ViewOf(folder, access);
        if (view is null || rulePlaces is not { Count: > 0 })
            return view;
        var children = new Dictionary<string, FolderAccessView>(
            view.Children ?? new Dictionary<string, FolderAccessView>(),
            StringComparer.FromComparison(comparison)
        );
        foreach (var item in listed.Where(item => item.Id is { } id && rulePlaces.ContainsKey(id)))
            children[item.Name] = View(At(item.Path, item.Id), null);
        return view with { Children = children };
    }

    private static FolderAccessView View(
        FolderAccess access,
        IReadOnlyDictionary<string, FolderAccessView>? children
    ) => new([.. access.Allowed.Order(StringComparer.Ordinal)], access.PathOnly, children);
}
