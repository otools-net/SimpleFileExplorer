using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Paths;
using Sfe.Core.Search;

namespace Sfe.Core.Files;

/// <summary>
/// Properties of a file or folder (agents/docs/features/properties.md): the disk's details at once, a folder's totals
/// from the index, and what costs reading the disk (a folder counted, a file's checksum) as jobs that the asker
/// follows and can stop.
/// </summary>
public sealed class Properties(IDbContextFactory<SfeDbContext> dbFactory, LibraryPaths paths)
{
    /// <summary>The details of the root-relative path; null when it is neither a file nor a folder.</summary>
    public ItemDetails? Describe(string path, string fullPath)
    {
        FileSystemInfo info = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
        if (!info.Exists)
            return null;
        var name = path.Length == 0 ? paths.RootName : info.Name;
        return new ItemDetails(
            path,
            name,
            info is DirectoryInfo,
            (info as FileInfo)?.Length,
            Known(info.CreationTimeUtc),
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            Known(info.LastAccessTimeUtc),
            name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden),
            info.Attributes.HasFlag(FileAttributes.ReadOnly),
            info.LinkTarget is not null
        );
    }

    /// <summary>
    /// The files, folders and bytes the index has below <paramref name="fullPath"/>, only where
    /// <paramref name="visible"/> lets the asker see (folder rules).
    /// </summary>
    public async Task<FolderTotals> IndexedTotalsAsync(
        string fullPath,
        PathFilter? visible,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var self = Path.TrimEndingDirectorySeparator(fullPath);
        var indexed = await db.Directories.AnyAsync(d => d.FullName == self, cancellationToken);
        var files = db
            .Files.AsNoTracking()
            .Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, fullPath))
            .WhereVisible(visible, f => f.FullName, paths);
        var folders = db
            .Directories.AsNoTracking()
            .Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, fullPath))
            .Where(d => d.FullName != self)
            .WhereVisible(visible, d => d.FullName, paths);
        return new FolderTotals(
            indexed,
            await files.LongCountAsync(cancellationToken),
            await folders.LongCountAsync(cancellationToken),
            await files.SumAsync(f => (long?)f.Size, cancellationToken) ?? 0
        );
    }

    /// <summary>A file system without the date answers 1601 (Windows) or 1970: told as unknown.</summary>
    private static DateTimeOffset? Known(DateTime utc) =>
        utc.Year <= 1970 ? null : new DateTimeOffset(utc, TimeSpan.Zero);
}
