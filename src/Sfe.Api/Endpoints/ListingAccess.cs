using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What a listing of the library tells about the asker's access to the folder listed and its entries
/// (agents/docs/features/item-access.md).
/// </summary>
internal static class ListingAccess
{
    private static readonly string[] FileRights =
    [
        .. RightCatalog.TenantRights.Where(r => r.ChangesFiles).Select(r => r.Id),
    ];

    /// <summary>
    /// <paramref name="listing"/> of <paramref name="path"/> (as asked) with what folder rules let the asker do there,
    /// when they restrict it (<see cref="AccessView"/>), and why it can't change files there, if it can't.
    /// </summary>
    public static DirectoryListing Mark(
        DirectoryListing listing,
        AccessState state,
        LibraryRequest library,
        WorkFolder workFolder,
        string? path
    )
    {
        var paths = library.Paths;
        var here = state.Access.FolderPolicy.IsUnrestricted ? null : state.At(paths, path);
        if (here is not null)
            listing = listing with { Access = AccessView(state, paths, listing.Path, here) };
        var reason = SfeReason(state, library.Options.CurrentValue.Rights.Disabled, here);
        if (reason is null && paths.TryResolve(listing.Path, out var full))
            reason = DiskReason(full, library.ServerFiles, workFolder);
        return listing with { ReadOnly = reason };
    }

    /// <summary>
    /// Why SFE lets the asker change no file where it has <paramref name="here"/>; null when it lets it.
    /// </summary>
    internal static ReadOnlyReason? SfeReason(
        AccessState state,
        IReadOnlyCollection<string> serverOff,
        FolderAccess? here
    )
    {
        if (!FileRights.Any(state.Has))
        {
            if (FileRights.All(r => RightCatalog.MatchesAny(serverOff, r)))
                return ReadOnlyReason.Server;
            var spaceOff = state.Access.SpaceDisabled ?? new HashSet<string>();
            if (FileRights.All(r => spaceOff.Contains(r) || RightCatalog.MatchesAny(serverOff, r)))
                return ReadOnlyReason.Space;
            return state.Access.IsGuest ? ReadOnlyReason.Visitor : ReadOnlyReason.Rights;
        }
        return here is null || FileRights.Any(here.Has) ? null : ReadOnlyReason.Rule;
    }

    /// <summary>
    /// Why the place itself takes no changes: a link the server keeps read-only, then what the file system says
    /// (<see cref="DiskWrites"/>, kept briefly per folder).
    /// </summary>
    private static ReadOnlyReason? DiskReason(string full, ServerFiles serverFiles, WorkFolder workFolder)
    {
        if (FileEndpoints.ProtectedFromWrites(full, serverFiles, workFolder))
            return ReadOnlyReason.Link;
        return DiskWrites.Of(full) switch
        {
            DiskWrite.ReadOnlyVolume => ReadOnlyReason.DiskReadOnly,
            DiskWrite.Refused => ReadOnlyReason.DiskRefused,
            _ => null,
        };
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
            .ToDictionary(c => c.Key, c => ViewOf(c.Value), LibraryPaths.PathComparer);
        var start = state.StartFolder(paths);
        return ViewOf(here, children, start.Length == 0 ? null : start);
    }

    private static FolderAccessView ViewOf(
        FolderAccess access,
        IReadOnlyDictionary<string, FolderAccessView>? children = null,
        string? start = null
    ) =>
        new(
            [.. access.Allowed.Order(StringComparer.Ordinal)],
            access.PathOnly,
            children,
            start,
            !FileRights.Any(access.Has)
        );
}
