using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Search;
using Sfe.Core.Tagging;

namespace Sfe.Core.FileTracking;

/// <summary>
/// Groups of indexed files with the same size and fingerprint. The fingerprint (<see cref="Indexing.FileFingerprint"/>)
/// covers the first megabyte only, so two files of a group are identical with near certainty, not proof: files that
/// differ only further in (e.g. edited tags at the end of an audio file of the same size) look alike.
/// </summary>
public sealed class DuplicateFinder(IDbContextFactory<SfeDbContext> dbFactory, LibraryPaths paths, MediaClassifier classifier, IOptionsMonitor<SfeOptions> options)
{
    public const int MaxGroupsPerPage = 500;

    /// <summary>Files listed per group; a group of thousands of identical files only reports how many there are.</summary>
    public const int MaxFilesPerGroup = 100;

    private DuplicateOptions Settings => options.CurrentValue.FileTracking.Duplicates;

    /// <param name="relativeDirectory">Only files of this folder and below are compared; empty for the whole library.</param>
    /// <param name="after"><see cref="DuplicateResults.Next"/> of the previous page; null for the first one.</param>
    /// <returns>null when <paramref name="relativeDirectory"/> or <paramref name="after"/> is invalid.</returns>
    /// <remarks>
    /// Biggest files first (the most space to win back), paged by key on (size, fingerprint) like searches: the
    /// <c>(Size, Sha1)</c> index gives the groups in order, so no page sorts or skips the whole index.
    /// </remarks>
    public async Task<DuplicateResults?> FindAsync(string? relativeDirectory, string? after, int? limit, CancellationToken cancellationToken = default)
    {
        if (!paths.TryResolve(relativeDirectory, out var directory))
            return null;
        (long Size, string Sha1)? cursor = null;
        if (after is not null)
        {
            if (ParseCursor(after) is not { } parsed)
                return null;
            cursor = parsed;
        }

        var pageSize = Math.Clamp(limit ?? Settings.GroupsPerPage, 1, MaxGroupsPerPage);
        var minimumSize = Math.Max(0, Settings.MinimumKilobytes) * 1024L;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var files = db.Files.AsNoTracking().Where(f => f.Sha1 != null && f.Size >= minimumSize);
        // The whole library needs no path test: leaving it out keeps the query on the index. Rows of another root
        // (the setting changed) are dropped below.
        var isRoot = paths.ToRelative(directory) is "";
        if (!isRoot)
            files = files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, directory));

        var groups = files
            .GroupBy(f => new { f.Size, f.Sha1 })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.Size, g.Key.Sha1, Count = g.Count() });

        DuplicateSummary? summary = null;
        if (cursor is null)
        {
            // Groups only (few next to the files): the totals are added up here.
            var all = await groups.Select(g => new { g.Size, g.Count }).ToListAsync(cancellationToken);
            var unfingerprinted = await db.Files.CountAsync(f => f.Sha1 == null, cancellationToken);
            summary = new DuplicateSummary(all.Count, all.Sum(g => g.Count), all.Sum(g => g.Size * (g.Count - 1)), unfingerprinted);
        }

        var paged = groups;
        if (cursor is var (afterSize, afterSha1))
            paged = paged.Where(g => g.Size < afterSize || (g.Size == afterSize && string.Compare(g.Sha1, afterSha1) < 0));
        var page = await paged
            .OrderByDescending(g => g.Size)
            .ThenByDescending(g => g.Sha1)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var results = new List<DuplicateGroup>(Math.Min(page.Count, pageSize));
        foreach (var group in page.Take(pageSize))
        {
            var groupFiles = files.Where(f => f.Size == group.Size && f.Sha1 == group.Sha1);
            var rows = await groupFiles
                .OrderBy(f => f.FullName)
                .Take(MaxFilesPerGroup)
                .Select(f => new { f.FullName, f.Extension, f.Size, f.LastWriteTime, TagIds = f.TagLinks.Select(l => l.TagId).ToList() })
                .ToListAsync(cancellationToken);
            var items = rows
                .Select(r => ToItem(r.FullName, r.Extension, r.Size, r.LastWriteTime, TagService.Summaries(graph, r.TagIds)))
                .OfType<LibraryItem>()
                .ToList();
            var count = isRoot ? group.Count - (rows.Count - items.Count) : group.Count;
            if (count > 1)
                results.Add(new DuplicateGroup(group.Size, group.Sha1!, count, group.Size * (count - 1), items));
        }

        var hasMore = page.Count > pageSize;
        return new DuplicateResults(
            Path: paths.ToRelative(directory) ?? string.Empty,
            Groups: results,
            HasMore: hasMore,
            Next: hasMore ? FormatCursor(page[pageSize - 1].Size, page[pageSize - 1].Sha1!) : null,
            Summary: summary,
            Fingerprinting: options.CurrentValue.Scan.ComputeHashes,
            MinimumSize: minimumSize,
            CanRemove: Settings.AllowRemove);
    }

    /// <summary>A file of the index as a listing item; null when it is outside the library.</summary>
    internal LibraryItem? ToItem(string fullName, string? extension, long size, DateTime lastWriteTime, IReadOnlyList<TagSummary> tags)
    {
        var path = paths.ToRelative(fullName);
        if (path is null)
            return null;
        var slash = path.LastIndexOf('/');
        return new LibraryItem(
            Name: Path.GetFileName(fullName),
            Path: path,
            Kind: classifier.Classify(extension),
            Extension: LibraryBrowser.NormalizeExtension(extension),
            Size: size,
            Modified: new DateTimeOffset(DateTime.SpecifyKind(lastWriteTime, DateTimeKind.Local)),
            Directory: slash < 0 ? string.Empty : path[..slash],
            Tags: tags);
    }

    private static string FormatCursor(long size, string sha1) => $"{size.ToString(CultureInfo.InvariantCulture)}:{sha1}";

    private static (long Size, string Sha1)? ParseCursor(string value)
    {
        var colon = value.IndexOf(':');
        return colon > 0
            && long.TryParse(value.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            && value.Length - colon - 1 is > 0 and <= 40
                ? (size, value[(colon + 1)..])
                : null;
    }
}

/// <param name="Fingerprint">The files' partial SHA1 (see <see cref="Indexing.FileFingerprint"/>).</param>
/// <param name="Count">Files in the group; <paramref name="Files"/> lists at most <see cref="DuplicateFinder.MaxFilesPerGroup"/>.</param>
/// <param name="WastedBytes">Space taken by all copies but one.</param>
public sealed record DuplicateGroup(long Size, string Fingerprint, int Count, long WastedBytes, IReadOnlyList<LibraryItem> Files);

/// <param name="Groups">Groups of two or more identical files.</param>
/// <param name="Files">Files in those groups.</param>
/// <param name="WastedBytes">Space taken by all copies but one of each group.</param>
/// <param name="Unfingerprinted">Indexed files (in the whole index) without a fingerprint, which cannot be compared.</param>
public sealed record DuplicateSummary(int Groups, int Files, long WastedBytes, int Unfingerprinted);

/// <param name="Next">Value of the <c>after</c> parameter that requests the next page; null on the last page.</param>
/// <param name="Summary">Totals over every page; only on the first page.</param>
/// <param name="Fingerprinting">Scans fingerprint files (<c>Scan:ComputeHashes</c>); without it no new file can be compared.</param>
/// <param name="MinimumSize">Smaller files are left out (bytes).</param>
/// <param name="CanRemove">Copies can be deleted (<c>POST /api/duplicates/remove</c>, see <see cref="DuplicateRemover"/>).</param>
public sealed record DuplicateResults(
    string Path,
    IReadOnlyList<DuplicateGroup> Groups,
    bool HasMore,
    string? Next,
    DuplicateSummary? Summary,
    bool Fingerprinting,
    long MinimumSize,
    bool CanRemove);
