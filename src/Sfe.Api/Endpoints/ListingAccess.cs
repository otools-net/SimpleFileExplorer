using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What a listing of the library tells about the asker's access to the folder listed and its entries.
/// </summary>
internal static class ListingAccess
{
    /// <summary>
    /// <paramref name="listing"/> of <paramref name="path"/> (as asked) with what folder rules let the asker do there,
    /// when they restrict it (<see cref="AccessView"/>).
    /// </summary>
    public static DirectoryListing Mark(DirectoryListing listing, AccessState state, LibraryPaths paths, string? path)
    {
        if (state.Access.FolderPolicy.IsUnrestricted || state.At(paths, path) is not { } here)
            return listing;
        return listing with { Access = AccessView(state, paths, listing.Path, here) };
    }

    /// <summary>
    /// What a subject restricted by folder rules may do in <paramref name="folder"/> and on its entries, and where its
    /// library starts: the client shows only what the asker may do here (tag, download, edit...).
    /// </summary>
    private static FolderAccessView AccessView(AccessState state, LibraryPaths paths, string folder, FolderAccess here)
    {
        var children = state
            .Access.FolderPolicy.ChildrenWithRules(folder, state.Access.Rights)
            .Where(c => c.Value.IsVisible)
            .ToDictionary(
                c => c.Key,
                c => new FolderAccessView([.. c.Value.Allowed.Order(StringComparer.Ordinal)], c.Value.PathOnly),
                LibraryPaths.PathComparer
            );
        var start = state.StartFolder(paths);
        return new FolderAccessView(
            [.. here.Allowed.Order(StringComparer.Ordinal)],
            here.PathOnly,
            children,
            start.Length == 0 ? null : start
        );
    }
}
