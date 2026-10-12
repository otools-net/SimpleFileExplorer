using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;

namespace Sfe.Core.FileTracking;

/// <summary>
/// Removes files from the index, keeping the tagged ones as <see cref="MissingFile"/>s first so that their tags can
/// follow them to where they turn up (<see cref="RelinkService"/>). Static, for the indexer: the relink service
/// depends on the tag service, which depends on the indexer.
/// </summary>
internal static class MissingFileRecorder
{
    /// <summary>
    /// Deletes <paramref name="delete"/> (files, or folders whose files go with them through cascade), after recording
    /// the tagged ones among <paramref name="files"/> as missing when <paramref name="remember"/> is set.
    /// </summary>
    /// <returns>Rows deleted by <paramref name="delete"/>.</returns>
    /// <remarks>
    /// One transaction: a crash in between would otherwise leave the files indexed and already recorded, and the next
    /// scan would record them a second time.
    /// </remarks>
    public static async Task<int> DeleteAsync(
        SfeDbContext db, IQueryable<FileEntry> files, Func<CancellationToken, Task<int>> delete, bool remember, CancellationToken cancellationToken)
    {
        if (!remember)
            return await delete(cancellationToken);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await RecordAsync(db, files, cancellationToken);
                var deleted = await delete(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return deleted;
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        });
    }

    private static async Task RecordAsync(SfeDbContext db, IQueryable<FileEntry> files, CancellationToken cancellationToken)
    {
        var tagged = await files
            .Where(f => f.TagLinks.Any())
            .Select(f => new { f.FullName, f.Name, f.Extension, f.Size, f.Sha1, f.LastWriteTime, f.RegisterTime, TagIds = f.TagLinks.Select(l => l.TagId).ToList() })
            .ToListAsync(cancellationToken);
        if (tagged.Count == 0)
            return;

        var now = DateTime.Now;
        foreach (var file in tagged)
        {
            db.MissingFiles.Add(new MissingFile
            {
                FullName = file.FullName,
                Name = file.Name,
                Extension = file.Extension,
                Size = file.Size,
                Sha1 = file.Sha1,
                LastWriteTime = file.LastWriteTime,
                RegisterTime = file.RegisterTime,
                MissingSince = now,
                TagLinks = file.TagIds.Select(id => new MissingFileTag { TagId = id }).ToList(),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
