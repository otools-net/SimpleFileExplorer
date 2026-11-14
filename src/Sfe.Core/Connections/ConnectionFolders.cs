using Sfe.Core.Access;
using Sfe.Core.Library;

namespace Sfe.Core.Connections;

/// <summary>
/// What a subject may do at each path of one connection: its <see cref="ConnectionPolicies"/> policy with the rights
/// it holds in the library of the request (<paramref name="granted"/>, the server's and library's switches already
/// applied). Paths are normalised (<see cref="RemotePaths.Normalize"/>) and compared by the provider's rule for case.
/// </summary>
public sealed class ConnectionFolders(FolderPolicy policy, IReadOnlySet<string> granted, StringComparison comparison)
{
    /// <summary>No folder rule nor level narrows the rights: an owner, a server administrator.</summary>
    public bool IsUnrestricted => policy.IsUnrestricted;

    /// <summary>How the provider compares names.</summary>
    public StringComparison Comparison => comparison;

    /// <summary>What the subject may do at <paramref name="path"/>.</summary>
    public FolderAccess At(string path) => policy.At(path, granted);

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

    private static FolderAccessView View(
        FolderAccess access,
        IReadOnlyDictionary<string, FolderAccessView>? children
    ) => new([.. access.Allowed.Order(StringComparer.Ordinal)], access.PathOnly, children);
}
