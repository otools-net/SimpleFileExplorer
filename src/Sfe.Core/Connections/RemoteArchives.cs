using System.IO.Compression;
using System.Text;
using Sfe.Core.Access;
using Sfe.Core.Files;

namespace Sfe.Core.Connections;

/// <summary>
/// Several files or folders of a connection as one zip, written while it is sent (the library's way,
/// <see cref="Archives"/>): each file is read from the provider into the zip, nothing kept on the server's disk.
/// Folders are listed page by page as the walk reaches them. At most <see cref="Archives.MaxItems"/> entries; files
/// without content (an online document) and what passes the limits are named in
/// <see cref="Archives.LeftOutEntry"/>; a zip passing the space's size limit is cut off (the download fails). What
/// folder rules hide from the person, or keep from downloading, is left out without being named.
/// </summary>
public static class RemoteArchives
{
    /// <summary>
    /// Checks the chosen paths at the provider; <paramref name="rootName"/> names a zip of the root. An item the
    /// provider names where <paramref name="folders"/> doesn't let the person download is missing.
    /// </summary>
    public static async Task<RemoteArchivePlanResult> PlanAsync(
        IRemoteStorage storage,
        IReadOnlyList<string?>? chosen,
        (string RootName, ConnectionFolders Folders) place,
        CancellationToken cancellationToken
    )
    {
        if (chosen is not { Count: > 0 } || chosen.Count > Archives.MaxItems)
            return new(null, $"Choose 1 to {Archives.MaxItems:N0} items.");
        var paths = chosen.Select(RemotePaths.Normalize).ToList();
        if (paths.Any(path => path is null))
            return new(null, "A path is not one of this connection.");

        var items = new List<RemoteArchiveItem>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Distinct().OfType<string>())
        {
            if (await storage.GetAsync(path, cancellationToken) is not { } item || !Readable(place.Folders, item))
                return new(null, Missing: true);
            var name = Archives.EntryName(path.Length == 0 ? place.RootName : item.Name);
            var free = name;
            for (var n = 1; !names.Add(free); n++)
                free = $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
            items.Add(new RemoteArchiveItem(path, free, item.IsFolder));
        }
        return new(new RemoteArchivePlan(items, FileName(items, place.RootName)));
    }

    /// <summary>
    /// What the person may put in a zip: a folder it sees or that leads to what it sees, a file it may view and
    /// download.
    /// </summary>
    public static bool Readable(ConnectionFolders folders, RemoteItem item)
    {
        var access = folders.At(item.Path);
        return item.IsFolder ? access.IsVisible : access.Has(Rights.ViewFiles) && access.Has(Rights.Download);
    }

    /// <param name="limits">The space's limit of a zip (<see cref="Archives.MaxBytes"/>; 0 = none), and where the
    /// person may read (checked again at every item).</param>
    public static async Task<RemoteArchiveSummary> WriteAsync(
        IRemoteStorage storage,
        RemoteArchivePlan plan,
        Stream output,
        (long MaxBytes, ConnectionFolders Folders) limits,
        CancellationToken cancellationToken
    )
    {
        var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, true, null, cancellationToken);
        await using (zip)
        {
            var walk = new Walk(storage, zip, limits, cancellationToken);
            foreach (var item in plan.Items)
            {
                if (item.IsFolder)
                    await walk.AddFolderAsync(item.Path, item.EntryName);
                else if (await storage.GetAsync(item.Path, cancellationToken) is { IsFolder: false } file)
                    await walk.AddFileAsync(file, item.EntryName);
            }
            if (walk.LeftOut.Count > 0)
            {
                var text = Encoding.UTF8.GetBytes(string.Join("\r\n", walk.LeftOut.Take(Archives.MaxLeftOutListed)));
                var entry = zip.CreateEntry(Archives.LeftOutEntry, CompressionLevel.Fastest);
                await using var target = await entry.OpenAsync(cancellationToken);
                await target.WriteAsync(text, cancellationToken);
            }
            return new RemoteArchiveSummary(walk.Files, walk.Bytes, walk.LeftOut);
        }
    }

    private static string FileName(IReadOnlyList<RemoteArchiveItem> items, string rootName)
    {
        if (items.Count == 1)
        {
            var only = items[0];
            return (only.IsFolder ? only.EntryName : Path.GetFileNameWithoutExtension(only.EntryName)) + ".zip";
        }
        var parents = items.Select(item => RemotePaths.Parent(item.Path)).Distinct().ToList();
        var parent = parents.Count == 1 ? parents[0] : "";
        return Archives.EntryName(parent.Length == 0 ? rootName : RemotePaths.NameOf(parent)) + ".zip";
    }

    /// <summary>The entries written so far, and the limits.</summary>
    private sealed class Walk(
        IRemoteStorage storage,
        ZipArchive zip,
        (long MaxBytes, ConnectionFolders Folders) limits,
        CancellationToken token
    )
    {
        private readonly long _maxBytes = limits.MaxBytes;

        private int _entries;

        public int Files { get; private set; }
        public long Bytes { get; private set; }
        public List<string> LeftOut { get; } = [];

        /// <summary>A folder and everything in it, folder by folder (no recursion: trees can be deep).</summary>
        public async Task AddFolderAsync(string path, string entryName)
        {
            var folders = new Queue<(string Path, string Entry)>([(path, entryName)]);
            while (folders.TryDequeue(out var folder))
            {
                if (!Count(folder.Path))
                    continue;
                zip.CreateEntry(folder.Entry + "/");
                string? cursor = null;
                do
                {
                    var page = await storage.ListAsync(folder.Path, cursor, token);
                    foreach (var item in page.Items.Where(item => Readable(limits.Folders, item)))
                    {
                        var entry = $"{folder.Entry}/{Archives.EntryName(item.Name)}";
                        if (item.IsFolder)
                            folders.Enqueue((item.Path, entry));
                        else
                            await AddFileAsync(item, entry);
                    }
                    cursor = page.Cursor;
                } while (cursor is not null);
            }
        }

        public async Task AddFileAsync(RemoteItem file, string entryName)
        {
            if (!Readable(limits.Folders, file))
                return;
            if (!file.Downloadable || !Count(file.Path))
            {
                if (!file.Downloadable)
                    LeftOut.Add(file.Path);
                return;
            }
            if (_maxBytes > 0 && Bytes + (file.Size ?? 0) > _maxBytes)
                throw new IOException($"The zip passes the limit of {_maxBytes / 1024 / 1024:N0} MB.");

            var level = Archives.Compressible.Contains(Path.GetExtension(file.Name))
                ? CompressionLevel.Fastest
                : CompressionLevel.NoCompression;
            var entry = zip.CreateEntry(entryName, level);
            if (file.Modified is { } modified)
                entry.LastWriteTime = Archives.ZipTime(modified.ToLocalTime());
            await using var content = await storage.OpenReadAsync(file.Path, null, token);
            await using var target = await entry.OpenAsync(token);
            await content.Stream.CopyToAsync(target, Archives.CopyBuffer, token);
            Files++;
            Bytes += file.Size ?? 0;
        }

        /// <summary>One more entry; false (and listed as left out) past <see cref="Archives.MaxItems"/>.</summary>
        private bool Count(string path)
        {
            if (++_entries <= Archives.MaxItems)
                return true;
            LeftOut.Add(path);
            return false;
        }
    }
}
