using Sfe.Core.Access;
using Sfe.Core.Library;
using Sfe.Core.Paths;

namespace Sfe.Api.Security;

/// <summary>
/// How a route takes folder rules into account (agents/docs/features/folder-access.md), read by the test checking
/// that every route about library paths says it; null <paramref name="How"/> for routes whose paths are not the
/// library's (server folders).
/// </summary>
public sealed record FolderMetadata(string? How);

/// <summary>What a route answers about one library path.</summary>
public enum PathVerdict
{
    Allowed,

    /// <summary>The subject sees the path but may not do that there (403).</summary>
    Forbidden,

    /// <summary>Not a path of the library, or one the subject doesn't see: answered as missing (404), never told apart.</summary>
    NotFound,
}

/// <summary>Folder rules at the routes: what the subject of the request may do at a library path, or where.</summary>
public static class FolderChecks
{
    /// <summary>The route checks its library paths against the folder rules itself; <paramref name="how"/> says how.</summary>
    public static TBuilder FolderScoped<TBuilder>(this TBuilder builder, string how) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new FolderMetadata(how));

    /// <summary>The route's paths are folders of the server, not of the library: folder rules don't apply.</summary>
    public static TBuilder NotLibraryPaths<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new FolderMetadata(null));

    /// <summary>
    /// What the subject may do at <paramref name="path"/> (root-relative, as the client sends it); null when it is
    /// not a path of the library. The path is checked as the file system opens it (<c>Private.</c> is
    /// <c>Private</c> on Windows, case as the host compares it), and also where it really leads (a link or junction
    /// inside the library, an 8.3 name): the stricter of the two applies.
    /// </summary>
    /// <remarks>Without folder rules nothing is read from the disk: every right of the space applies.</remarks>
    public static FolderAccess? At(this AccessState state, LibraryPaths paths, string? path)
    {
        if (state.Access.FolderPolicy.IsUnrestricted)
            return state.Access.At(string.Empty);
        if (!paths.TryResolve(path, out var full) || paths.ToRelative(full) is not { } relative)
            return null;

        var access = state.Access.At(relative);
        string? real;
        try
        {
            real = paths.RealRelative(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            real = null;
        }
        return real is not null && !LibraryPaths.PathComparer.Equals(real, relative) ? access.And(state.Access.At(real)) : access;
    }

    /// <summary>Whether the subject may use <paramref name="right"/> at <paramref name="path"/>.</summary>
    public static PathVerdict Check(this AccessState state, LibraryPaths paths, string? path, string right) =>
        state.At(paths, path) is not { } access ? PathVerdict.NotFound
        : access.Has(right) ? PathVerdict.Allowed
        : access.Has(Rights.Browse) ? PathVerdict.Forbidden
        : PathVerdict.NotFound;

    /// <summary>The subject may use <paramref name="right"/> at every path of <paramref name="list"/>.</summary>
    public static bool AllowsAll(this AccessState state, LibraryPaths paths, IEnumerable<string?> list, string right) =>
        state.Access.FolderPolicy.IsUnrestricted || list.All(path => state.Check(paths, path, right) == PathVerdict.Allowed);

    /// <summary>Where the subject may see files, for the queries over the index; null when everywhere (no condition to add).</summary>
    public static PathFilter? Visible(this AccessState state) =>
        state.Access.FilterFor(Rights.Browse) is { IsEverything: false } filter ? filter : null;

    /// <summary>
    /// What a listing of <paramref name="folder"/> (canonical, root-relative) leaves out: the children the subject
    /// doesn't see, or all but those leading to what it sees when the folder is only on the way there. Null when
    /// nothing is left out.
    /// </summary>
    public static ListingFilter? ListingFilterOf(this AccessState state, string folder, FolderAccess access)
    {
        if (state.Access.FolderPolicy.IsUnrestricted)
            return null;
        var children = state.Access.FolderPolicy.ChildrenWithRules(folder, state.Access.Rights);
        var hidden = children.Where(c => !c.Value.IsVisible).Select(c => c.Key).ToHashSet(LibraryPaths.PathComparer);
        if (access.PathOnly)
            return new ListingFilter(hidden, children.Where(c => c.Value.IsVisible).Select(c => c.Key).ToHashSet(LibraryPaths.PathComparer));
        return hidden.Count == 0 ? null : new ListingFilter(hidden);
    }

    /// <summary>
    /// The items of a page of index results the subject sees where they really are. Scans follow folder links, so an
    /// index row's path can lead through a link into a folder the subject doesn't see: the query's filter can't tell,
    /// this check can (one look at the disk per item, for subjects folder rules restrict only).
    /// </summary>
    public static IReadOnlyList<LibraryItem> Seen(this AccessState state, LibraryPaths paths, IReadOnlyList<LibraryItem> items) =>
        state.Access.FolderPolicy.IsUnrestricted ? items : [.. items.Where(item => state.Sees(paths, item.Path))];

    /// <summary>The subject may see the file or folder at <paramref name="relativePath"/> itself (not only pass through it).</summary>
    public static bool Sees(this AccessState state, LibraryPaths paths, string relativePath) =>
        state.Access.FolderPolicy.IsUnrestricted || state.At(paths, relativePath)?.Has(Rights.Browse) == true;
}
