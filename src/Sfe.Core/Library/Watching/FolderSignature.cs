namespace Otools.Net.Sfe.Core.Library.Watching;

/// <summary>
/// What a polled folder holds, in two numbers: its entries counted and a hash of each one's name, size, last write and
/// attributes, combined so that their order doesn't matter (a share may list them in another order each time). Items
/// being written by SFE and the work folder are left out, as the watcher leaves them out.
/// </summary>
internal readonly record struct FolderSignature(long Hash, int Count)
{
    private static readonly EnumerationOptions Everything = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        RecurseSubdirectories = false,
    };

    /// <summary>The signature of <paramref name="fullPath"/> now; null when it can't be read (gone, refused).</summary>
    /// <param name="top">The library's own folder, where the work folder may be.</param>
    public static FolderSignature? Read(string fullPath, bool top)
    {
        try
        {
            long hash = 0;
            var count = 0;
            foreach (var entry in new DirectoryInfo(fullPath).EnumerateFileSystemInfos("*", Everything))
            {
                if (FolderWatchers.IsIgnored(entry.Name, top))
                    continue;
                var length = entry is FileInfo file ? file.Length : -1;
                var low = HashCode.Combine(entry.Name, length, entry.LastWriteTimeUtc.Ticks, entry.Attributes);
                var high = HashCode.Combine(low, entry.Name.Length, length);
                hash ^= ((long)high << 32) | (uint)low;
                count++;
            }
            return new FolderSignature(hash, count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
