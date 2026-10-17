using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Paths;

namespace Sfe.Core.Access;

/// <param name="Path">Relative to the library root ('/' separated, empty for the root).</param>
/// <param name="IsFolder">A folder, else a file.</param>
public sealed record QuickLinkEntry(string Path, bool IsFolder);

public enum QuickLinkAddStatus
{
    Done,

    /// <summary>The list would hold more than <see cref="QuickLinkService.MaxLinks"/> links: nothing was changed.</summary>
    Full,
}

/// <summary>
/// The quick links of each user in each library: a short ordered list of folders and files kept in the sidebar.
/// Paths are compared as the host file system does (<see cref="LibraryPaths.PathComparer"/>); whether they exist is
/// the caller's business, which knows the library.
/// </summary>
public sealed class QuickLinkService(IDbContextFactory<AccessDbContext> dbFactory)
{
    /// <summary>A sidebar list, not a bookmark store: every list is read and checked on the disk at each page load.</summary>
    public const int MaxLinks = 100;

    public async Task<IReadOnlyList<QuickLinkEntry>> GetAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return [.. (await LoadAsync(db, userId, tenantId, cancellationToken)).Select(ToEntry)];
    }

    /// <summary>
    /// Adds <paramref name="links"/> before the link now at <paramref name="position"/> (null or past the end: at the
    /// end), in their order. Links already in the list move there when a position is given, and stay where they are
    /// otherwise; their kind stays the one recorded.
    /// </summary>
    public async Task<(QuickLinkAddStatus Status, IReadOnlyList<QuickLinkEntry> Links)> AddAsync(
        int userId, int tenantId, IReadOnlyList<QuickLinkEntry> links, int? position, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await LoadAsync(db, userId, tenantId, cancellationToken);
        var wanted = links.DistinctBy(l => l.Path, LibraryPaths.PathComparer).ToList();
        var added = wanted.Count(l => Find(rows, l.Path) is null);
        if (added > 0 && rows.Count + added > MaxLinks)
            return (QuickLinkAddStatus.Full, [.. rows.Select(ToEntry)]);

        // The links moved, and the first link from `position` on that stays: the others go before it.
        var moved = position is null ? [] : wanted.Select(l => Find(rows, l.Path)).OfType<QuickLink>().ToHashSet();
        var anchor = position is { } index ? rows.Skip(Math.Max(index, 0)).FirstOrDefault(r => !moved.Contains(r)) : null;
        var placed = new List<QuickLink>();
        foreach (var link in wanted)
        {
            if (Find(rows, link.Path) is { } existing)
            {
                if (!moved.Contains(existing))
                    continue;
                rows.Remove(existing);
                placed.Add(existing);
            }
            else
            {
                var row = new QuickLink { UserId = userId, TenantId = tenantId, Path = link.Path, IsFolder = link.IsFolder };
                db.QuickLinks.Add(row);
                placed.Add(row);
            }
        }

        var at = anchor is null ? rows.Count : rows.IndexOf(anchor);
        rows.InsertRange(at, placed);
        Renumber(rows);
        await db.SaveChangesAsync(cancellationToken);
        return (QuickLinkAddStatus.Done, [.. rows.Select(ToEntry)]);
    }

    /// <summary>Removes the link to <paramref name="path"/>; the list as it is afterwards.</summary>
    public async Task<IReadOnlyList<QuickLinkEntry>> RemoveAsync(int userId, int tenantId, string path, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await LoadAsync(db, userId, tenantId, cancellationToken);
        var removed = rows.Where(r => LibraryPaths.PathComparer.Equals(r.Path, path)).ToList();
        if (removed.Count > 0)
        {
            db.QuickLinks.RemoveRange(removed);
            rows.RemoveAll(removed.Contains);
            Renumber(rows);
            await db.SaveChangesAsync(cancellationToken);
        }
        return [.. rows.Select(ToEntry)];
    }

    /// <summary>
    /// Every user's links to <paramref name="from"/> and to what is below it follow it to <paramref name="to"/> (a
    /// file or folder renamed or moved in the library; root-relative paths).
    /// </summary>
    /// <returns>Links changed.</returns>
    public async Task<int> MovePathsAsync(int tenantId, string from, string to, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // A hundred links per user at most: read the library's and compare as the host's file system does.
        var rows = (await db.QuickLinks.Where(l => l.TenantId == tenantId).ToListAsync(cancellationToken))
            .Where(l => FolderPolicy.IsSameOrBelow(l.Path, from) && from.Length > 0)
            .ToList();
        foreach (var row in rows)
            row.Path = to + row.Path[from.Length..];
        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    /// <summary>The user's rows in order, tracked; duplicates left by two concurrent adds are dropped.</summary>
    private static async Task<List<QuickLink>> LoadAsync(AccessDbContext db, int userId, int tenantId, CancellationToken cancellationToken)
    {
        var rows = await db.QuickLinks
            .Where(l => l.UserId == userId && l.TenantId == tenantId)
            .OrderBy(l => l.Position).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
        var seen = new HashSet<string>(LibraryPaths.PathComparer);
        var duplicates = rows.Where(r => !seen.Add(r.Path)).ToList();
        if (duplicates.Count > 0)
        {
            db.QuickLinks.RemoveRange(duplicates);
            rows.RemoveAll(duplicates.Contains);
        }
        return rows;
    }

    private static QuickLink? Find(List<QuickLink> rows, string path) =>
        rows.FirstOrDefault(r => LibraryPaths.PathComparer.Equals(r.Path, path));

    private static void Renumber(List<QuickLink> rows)
    {
        for (var i = 0; i < rows.Count; i++)
            rows[i].Position = i;
    }

    private static QuickLinkEntry ToEntry(QuickLink row) => new(row.Path, row.IsFolder);
}
