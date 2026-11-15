using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// The tags of the library's files a transfer carries (<see cref="LibraryTransferEndpoint"/>): read from a file
/// leaving for a connection, given to a file arriving from one once it is indexed. A file the index doesn't hold has
/// none to give and gets none.
/// </summary>
public sealed class LibraryTransferTags(IDbContextFactory<SfeDbContext> dbFactory, LibraryPaths paths, TagService tags)
{
    /// <summary>The tags of the file at <paramref name="path"/> (relative to the library's root).</summary>
    public async Task<IReadOnlyList<int>> TagIdsAsync(string path, CancellationToken cancellationToken)
    {
        if (!paths.TryResolve(path, out var full))
            return [];
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .FileTags.Where(l => l.File!.FullName == full)
            .Select(l => l.TagId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Gives <paramref name="tagIds"/> (those that still exist) to the indexed file at
    /// <paramref name="path"/>.</summary>
    /// <returns>False when the index has no row of it.</returns>
    public async Task<bool> AddAsync(string path, IReadOnlyList<int> tagIds, CancellationToken cancellationToken)
    {
        if (!paths.TryResolve(path, out var full))
            return false;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var fileId = await db
            .Files.Where(f => f.FullName == full)
            .Select(f => (int?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (fileId is not { } id)
            return false;
        var known = await db.Tags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
        if (await FileTagRows.AddAsync(db, [id], known, cancellationToken) > 0)
            tags.InvalidateFileCounts();
        return true;
    }
}
