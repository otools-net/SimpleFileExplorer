using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Search;
using Otools.Net.Sfe.Core.Thumbnails;
using Otools.Net.Sfe.Core.Trickplay;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The writing of the library's copies (<see cref="FileJobs"/>, agents/docs/features/move-and-copy.md): a file, or a
/// folder's tree (links never followed, work folders never copied, folders the person may not see left out), then
/// what SFE knows of the originals given to the copies: previews, tags when asked, index rows.
/// </summary>
public sealed class LibraryCopies(
    IDbContextFactory<SfeDbContext> dbFactory,
    WorkFolder work,
    DirectoryIndexer indexer,
    LibraryBrowser browser,
    ThumbnailService thumbnails,
    TrickplayService trickplay,
    ILogger<FileJobs> logger
)
{
    private const int CopyBuffer = 1 << 20;

    /// <summary>
    /// Copies the folder <paramref name="source"/> into <paramref name="destination"/>, made here; what is left out
    /// (links, work folders, folders the person may not see) is added to <paramref name="skipped"/>.
    /// </summary>
    internal async Task FolderAsync(
        JobRun job,
        string source,
        string sourcePath,
        string destination,
        List<string> skipped,
        CancellationToken token
    )
    {
        Directory.CreateDirectory(destination);
        foreach (
            var entry in new DirectoryInfo(source).EnumerateFileSystemInfos(
                "*",
                new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }
            )
        )
        {
            token.ThrowIfCancellationRequested();
            var entryPath = LibraryClashes.Join(sourcePath, entry.Name);
            // Links are never followed (nor made, which needs a privilege on Windows); work folders never copied.
            if (entry.LinkTarget is not null || work.Contains(entry.FullName))
            {
                skipped.Add(entryPath);
                continue;
            }
            if (entry is DirectoryInfo)
            {
                // A folder the person may not see (or whose files they may not open) is left out.
                if (!await job.Access.MayAsync(entryPath, Rights.ViewFiles))
                {
                    skipped.Add(entryPath);
                    continue;
                }
                await FolderAsync(
                    job,
                    entry.FullName,
                    entryPath,
                    Path.Combine(destination, entry.Name),
                    skipped,
                    token
                );
            }
            else
            {
                await FileAsync(job, entry.FullName, Path.Combine(destination, entry.Name), token);
            }
        }
    }

    /// <summary>
    /// Copies the file <paramref name="source"/> to <paramref name="destination"/>, counting its bytes, with its
    /// modification time; nothing half written stays.
    /// </summary>
    internal static async Task FileAsync(JobRun job, string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBuffer,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        try
        {
            await using var output = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                CopyBuffer,
                FileOptions.Asynchronous
            );
            var buffer = new byte[CopyBuffer];
            int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                job.AddBytes(read);
            }
        }
        catch when (File.Exists(destination))
        {
            File.Delete(destination);
            throw;
        }
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
    }

    /// <summary>
    /// The copy <paramref name="destination"/> of <paramref name="source"/>, whole and named: indexed (a file now, a
    /// folder added to <paramref name="copiedFolders"/> for a scan), with the originals' previews and, when asked,
    /// their tags.
    /// </summary>
    internal async Task AfterAsync(
        JobRun job,
        string source,
        string destination,
        bool isFolder,
        List<string> copiedFolders,
        string destinationPath,
        CancellationToken token
    )
    {
        browser.Invalidate(Path.GetDirectoryName(destination)!);
        await using var db = await dbFactory.CreateDbContextAsync(token);
        // Previews and tags come from the originals' index rows (at most what was indexed).
        var originals = await db
            .Files.AsNoTracking()
            .Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, source))
            .Select(f => new { f.Id, f.FullName })
            .ToListAsync(token);
        foreach (var original in originals)
            CopyCache(original.FullName, destination + original.FullName[source.Length..]);

        if (job.Request.KeepTags)
        {
            var ids = originals.Select(o => o.Id).ToList();
            var tagged = (
                await db
                    .FileTags.AsNoTracking()
                    .Where(l => ids.Contains(l.FileId))
                    .Select(l => new { l.FileId, l.TagId })
                    .ToListAsync(token)
            )
                .GroupBy(l => l.FileId)
                .ToDictionary(
                    g => destination + originals.First(o => o.Id == g.Key).FullName[source.Length..],
                    g => g.Select(l => l.TagId).ToList(),
                    LibraryPaths.PathComparer
                );
            if (tagged.Count > 0)
            {
                var copies = await indexer.IndexFilesAsync([.. tagged.Keys], token);
                // Indexed, the copies may have the default tags of their folder already.
                var copyIds = copies.Values.ToList();
                var existing = (
                    await db
                        .FileTags.AsNoTracking()
                        .Where(l => copyIds.Contains(l.FileId))
                        .Select(l => new { l.FileId, l.TagId })
                        .ToListAsync(token)
                )
                    .Select(l => (l.FileId, l.TagId))
                    .ToHashSet();
                foreach (var (file, tagIds) in tagged)
                {
                    if (copies.TryGetValue(file, out var fileId))
                    {
                        foreach (var tagId in tagIds.Where(t => !existing.Contains((fileId, t))))
                            db.FileTags.Add(new FileTag { FileId = fileId, TagId = tagId });
                    }
                }
                await db.SaveChangesAsync(token);
            }
        }

        if (isFolder)
            copiedFolders.Add(destinationPath);
        else
            await indexer.IndexFilesAsync([destination], token);
    }

    /// <summary>
    /// The thumbnail and trickplay of a copy are those of its original (same content, same modification time).
    /// </summary>
    private void CopyCache(string original, string copy)
    {
        try
        {
            var thumbnail = thumbnails.GetCachePath(original);
            if (File.Exists(thumbnail))
            {
                var target = thumbnails.GetCachePath(copy);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(thumbnail, target, overwrite: true);
            }
            var frames = trickplay.GetCacheFolder(original);
            if (Directory.Exists(frames))
            {
                var target = trickplay.GetCacheFolder(copy);
                Directory.CreateDirectory(target);
                foreach (var file in Directory.EnumerateFiles(frames))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not copy the cached previews of {File}", original);
        }
    }
}
