using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The names of uploads into a connection: a name or, for a folder upload, a path below the target folder whose
/// every part is a name any provider takes; and which of them are already taken there.
/// </summary>
internal static class ConnectionUploadNames
{
    /// <summary>
    /// The name as a path below the folder (<c>\</c> read as <c>/</c>, as the library's uploads do); null when a
    /// part is not a valid name or there are more than <see cref="Uploads.MaxDepth"/> folders on the way.
    /// </summary>
    public static string? PathOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        var parts = name.Replace('\\', '/').Split('/');
        if (parts.Length > Uploads.MaxDepth + 1 || !parts.All(RemotePaths.IsValidName))
            return null;
        return RemotePaths.Normalize(string.Join('/', parts));
    }

    /// <summary>
    /// The names of <paramref name="names"/> already taken in <paramref name="folder"/>. The folder is listed once
    /// (up to <see cref="RemoteListings.MaxItems"/> items); a path below one of its folders, or a name when the
    /// folder is larger, is asked of the provider.
    /// </summary>
    public static async Task<IReadOnlyList<string>> TakenAsync(
        IRemoteStorage storage,
        string folder,
        IReadOnlyList<string> names,
        ProviderCapabilities capabilities,
        CancellationToken cancellationToken
    )
    {
        var listed = await ListAsync(storage, folder, capabilities, cancellationToken);
        var taken = new List<string>();
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            if (PathOf(name) is { } path && await IsTakenAsync(storage, folder, path, listed, cancellationToken))
                taken.Add(name);
        }
        return taken;
    }

    private static async Task<bool> IsTakenAsync(
        IRemoteStorage storage,
        string folder,
        string path,
        FolderNames listed,
        CancellationToken cancellationToken
    )
    {
        var first = path.Split('/')[0];
        if (listed.IsWhole)
        {
            var isName = first.Length == path.Length;
            // Nothing of that name, or a file where the path needs a folder.
            if (!listed.Names.TryGetValue(first, out var isFolder) || (!isName && !isFolder))
                return false;
            if (isName)
                return true;
        }
        return await storage.GetAsync(RemotePaths.Combine(folder, path), cancellationToken) is not null;
    }

    private static async Task<FolderNames> ListAsync(
        IRemoteStorage storage,
        string folder,
        ProviderCapabilities capabilities,
        CancellationToken cancellationToken
    )
    {
        var names = new Dictionary<string, bool>(RemotePaths.ComparerOf(capabilities));
        string? cursor = null;
        do
        {
            var page = await storage.ListAsync(folder, cursor, cancellationToken);
            foreach (var item in page.Items)
                names[item.Name] = item.IsFolder;
            cursor = page.Cursor;
        } while (cursor is not null && names.Count < RemoteListings.MaxItems);
        return new FolderNames(names, IsWhole: cursor is null);
    }

    /// <param name="Names">Each item's name, with whether it is a folder.</param>
    /// <param name="IsWhole">The folder was read to its end.</param>
    private sealed record FolderNames(Dictionary<string, bool> Names, bool IsWhole);
}
