using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The children of one folder of a connection's index for one listing: its folders, then its files, without the
/// rows marked removed (gone at the provider, deleted when the sync ends) and without what folder rules hide from the
/// subject (by key and by provider id, in the queries: counts and later pages never hold them). Each page costs its
/// own rows: read by key after the last row of the page before, or by offset when that row is not known.
/// </summary>
internal sealed class IndexListingRows
{
    private readonly IQueryable<IndexListingRow> _folders;
    private readonly IQueryable<IndexListingRow> _files;
    private readonly (SortField Sort, bool Descending) _folderSort;
    private readonly (SortField Sort, bool Descending) _fileSort;

    /// <param name="folderId">The folder's row.</param>
    public IndexListingRows(SfeDbContext db, IndexListingQuery asked, long folderId, IndexListingFilter keep)
    {
        var id = asked.Target.ConnectionId;
        var folders = db
            .RemoteDirectories.Where(d => d.ConnectionId == id && d.ParentId == folderId && !d.Removed)
            .Select(d => new IndexListingRow
            {
                IsFolder = true,
                FullName = d.FullName,
                DisplayPath = d.DisplayPath,
                NameKey = d.NameKey,
                Modified = d.LastWriteTime,
                ProviderId = d.ProviderId,
                Downloadable = true,
            });
        var files = db
            .RemoteFiles.Where(f => f.ConnectionId == id && f.DirectoryId == folderId && !f.Removed)
            .Select(f => new IndexListingRow
            {
                FullName = f.FullName,
                DisplayPath = f.DisplayPath,
                NameKey = f.NameKey,
                Extension = f.Extension,
                Size = f.Size,
                Modified = f.LastWriteTime,
                ProviderId = f.ProviderId,
                Hash = f.ContentHash,
                Downloadable = f.Downloadable,
            });
        _folders = keep.Apply(folders);
        _files = keep.Apply(files);
        var sort = asked.Query.Sort;
        _folderSort = IndexListingOrder.OfFolders(sort, asked.Query.Descending);
        _fileSort = (IndexListingOrder.OfFiles(sort), asked.Query.Descending);
    }

    /// <summary>Folders and files shown, counted on the indexes' ranges.</summary>
    public async Task<IndexListingCounts> CountAsync(CancellationToken cancellationToken) =>
        new(await _folders.CountAsync(cancellationToken), await _files.CountAsync(cancellationToken));

    /// <summary>
    /// <paramref name="take"/> rows from <paramref name="offset"/>: after <paramref name="last"/> when it is known
    /// (the last row of the page before), else by offset (<paramref name="folders"/> tells where files begin).
    /// </summary>
    public async Task<List<IndexListingRow>> PageAsync(
        (int Offset, int Take) wanted,
        IndexListingRow? last,
        int folders,
        CancellationToken cancellationToken
    )
    {
        var (offset, take) = wanted;
        if (last is { IsFolder: false })
            return await ReadAsync(_files, _fileSort, (last, 0, take), cancellationToken);
        var page = new List<IndexListingRow>(take);
        if (last is not null || offset < folders)
            page.AddRange(
                await ReadAsync(_folders, _folderSort, (last, last is null ? offset : 0, take), cancellationToken)
            );
        if (page.Count >= take)
            return page;
        var skip = last is null && page.Count == 0 ? Math.Max(0, offset - folders) : 0;
        page.AddRange(await ReadAsync(_files, _fileSort, (null, skip, take - page.Count), cancellationToken));
        return page;
    }

    private static async Task<List<IndexListingRow>> ReadAsync(
        IQueryable<IndexListingRow> rows,
        (SortField Sort, bool Descending) order,
        (IndexListingRow? Last, int Skip, int Take) wanted,
        CancellationToken cancellationToken
    )
    {
        var (sort, descending) = order;
        var (last, skip, take) = wanted;
        if (last is null)
        {
            var ordered = rows.Ordered(sort, descending);
            return await (skip > 0 ? ordered.Skip(skip) : ordered).Take(take).ToListAsync(cancellationToken);
        }
        var read = new List<IndexListingRow>(take);
        foreach (var after in rows.After(sort, descending, last))
        {
            if (read.Count >= take)
                break;
            var next = after.Ordered(sort, descending).Take(take - read.Count);
            read.AddRange(await next.ToListAsync(cancellationToken));
        }
        return read;
    }
}
