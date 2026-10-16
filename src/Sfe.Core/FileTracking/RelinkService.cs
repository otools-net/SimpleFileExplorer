using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Core.FileTracking;

/// <summary>
/// Gives the tags of missing files (tagged files a scan no longer found, see <see cref="MissingFileRecorder"/>) back
/// to them where they turned up: moved, renamed, or at the same place once their drive is back.
/// </summary>
/// <remarks>
/// Scans visit folders in no useful order, so a moved file can be indexed at its new place before or after its old
/// place is found empty, in the same scan or another one. Matching therefore runs after each scan
/// (<see cref="ScanCoordinator"/>) over every missing file, rather than when a file disappears.
/// </remarks>
public sealed class RelinkService(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    DirectoryIndexer indexer,
    TagService tags,
    DuplicateFinder finder,
    ThumbnailService thumbnails,
    TrickplayService trickplay,
    IOptionsMonitor<SfeOptions> options,
    ILogger<RelinkService> logger)
{
    public const int MaxPageSize = 500;
    public const int DefaultPageSize = 100;

    /// <summary>Candidates listed per missing file.</summary>
    public const int MaxCandidates = 10;

    /// <summary>Missing files matched per round trip in a pass.</summary>
    private const int BatchSize = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private RelinkOptions Settings => options.CurrentValue.FileTracking.Relink;

    /// <summary>
    /// Relinks every missing file that has exactly one likely new place, and forgets those kept longer than
    /// <see cref="RelinkOptions.RetentionDays"/>. Does nothing while relinking is off.
    /// </summary>
    public async Task<RelinkPassResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var settings = Settings;
        if (!settings.Enabled)
            return new RelinkPassResult(0, 0, 0);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var forgotten = 0;
            if (settings.RetentionDays > 0)
            {
                var limit = DateTime.Now.AddDays(-settings.RetentionDays);
                forgotten = await db.MissingFiles.Where(m => m.MissingSince < limit).ExecuteDeleteAsync(cancellationToken);
            }

            var relinked = 0;
            var used = new HashSet<int>();
            var lastId = 0;
            while (true)
            {
                var batch = await db.MissingFiles
                    .AsNoTracking()
                    .Where(m => m.Id > lastId)
                    .OrderBy(m => m.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);
                if (batch.Count == 0)
                    break;
                lastId = batch[^1].Id;

                var candidates = await CandidatesAsync(db, batch, settings.MatchByName, cancellationToken);
                foreach (var missing in batch)
                {
                    if (Choose(missing, candidates[missing.Id], used) is not { } target)
                        continue;
                    used.Add(target.Id);
                    await RelinkAsync(db, missing, target, cancellationToken);
                    relinked++;
                }
            }

            var remaining = await db.MissingFiles.CountAsync(cancellationToken);
            if (relinked + forgotten > 0)
                logger.LogInformation("Relinked {Relinked} moved files, forgot {Forgotten} missing for too long; {Remaining} still missing", relinked, forgotten, remaining);
            return new RelinkPassResult(relinked, forgotten, remaining);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The new place of a missing file: among the files indexed after it (a moved file is indexed again; an older
    /// identical file is another copy), the only one, or else the only one with its name.
    /// </summary>
    private static Candidate? Choose(MissingFile missing, IEnumerable<Candidate> candidates, HashSet<int> used)
    {
        var later = candidates.Where(c => c.RegisterTime > missing.RegisterTime && !used.Contains(c.Id)).ToList();
        if (later.Count > 1)
            later = later.Where(c => c.SameName).ToList();
        return later.Count == 1 ? later[0] : null;
    }

    /// <summary>Missing files to relink, oldest first, with the indexed files that may be them.</summary>
    /// <param name="after"><see cref="MissingFileResults.Next"/> of the previous page.</param>
    /// <param name="visible">
    /// Where the asker may see files (folder rules): only the missing files that were there, and the candidates that
    /// are there, are listed (a missing file outside the library's folder is never listed then); null = everywhere.
    /// </param>
    public async Task<MissingFileResults> ListAsync(int? after, int? limit, CancellationToken cancellationToken = default, PathFilter? visible = null)
    {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.MissingFiles.AsNoTracking().WhereVisible(visible, m => m.FullName, paths);
        var total = after is null ? await query.CountAsync(cancellationToken) : (int?)null;
        if (after is { } afterId)
            query = query.Where(m => m.Id > afterId);
        var page = await query
            .OrderBy(m => m.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var files = page.Take(pageSize).ToList();

        var ids = files.Select(m => m.Id).ToList();
        var links = (await db.MissingFileTags
                .Where(l => ids.Contains(l.MissingFileId))
                .Select(l => new { l.MissingFileId, l.TagId })
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.MissingFileId, l => l.TagId);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var candidates = await CandidatesAsync(db, files, Settings.MatchByName, cancellationToken);

        var items = files.Select(m => new MissingFileInfo(
            m.Id,
            paths.ToRelative(m.FullName) ?? m.FullName,
            Path.GetFileName(m.FullName),
            m.Size,
            new DateTimeOffset(DateTime.SpecifyKind(m.LastWriteTime, DateTimeKind.Local)),
            new DateTimeOffset(DateTime.SpecifyKind(m.MissingSince, DateTimeKind.Local)),
            TagService.Summaries(graph, links[m.Id]),
            candidates[m.Id]
                .Where(c => visible is null || (paths.ToRelative(c.FullName) is { } relative && visible.Includes(relative)))
                .OrderByDescending(c => c.SameName)
                .ThenBy(c => c.FullName, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxCandidates)
                .Select(c => finder.ToItem(c.FullName, c.Extension, c.Size, c.LastWriteTime, TagService.Summaries(graph, c.TagIds)))
                .OfType<LibraryItem>()
                .ToList()))
            .ToList();

        var hasMore = page.Count > pageSize;
        return new MissingFileResults(items, total, hasMore, hasMore ? files[^1].Id : null, Settings.Enabled);
    }

    /// <summary>Gives the tags of a missing file to a file of the library chosen by the user, and forgets the missing file.</summary>
    /// <param name="relativePath">Any existing file: it is indexed if needed.</param>
    /// <param name="visible">Where the asker may see files: a missing file that was elsewhere is not found.</param>
    public async Task<RelinkStatus> RelinkAsync(int missingId, string? relativePath, CancellationToken cancellationToken = default, PathFilter? visible = null)
    {
        if (!paths.TryResolve(relativePath, out var fullPath) || !File.Exists(fullPath))
            return RelinkStatus.InvalidPath;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.MissingFiles.WhereVisible(visible, m => m.FullName, paths).AnyAsync(m => m.Id == missingId, cancellationToken))
            return RelinkStatus.NotFound;

        var ids = await indexer.IndexFilesAsync([fullPath], cancellationToken);
        if (!ids.TryGetValue(fullPath, out var fileId))
            return RelinkStatus.InvalidPath;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var target = await db.Files
                .Where(f => f.Id == fileId)
                .Select(f => new Candidate(f.Id, f.FullName, f.Extension, f.Size, f.LastWriteTime, f.RegisterTime, f.Sha1, true, new List<int>()))
                .FirstAsync(cancellationToken);
            // A pass may have relinked it in the meantime.
            if (await db.MissingFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == missingId, cancellationToken) is not { } missing)
                return RelinkStatus.NotFound;
            await RelinkAsync(db, missing, target, cancellationToken);
            return RelinkStatus.Relinked;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets a missing file and its tags (all of them when <paramref name="missingId"/> is null).</summary>
    /// <param name="visible">Where the asker may see files: only the missing files that were there are forgotten.</param>
    /// <returns>Missing files forgotten.</returns>
    public async Task<int> ForgetAsync(int? missingId, CancellationToken cancellationToken = default, PathFilter? visible = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = (missingId is { } id ? db.MissingFiles.Where(m => m.Id == id) : db.MissingFiles).WhereVisible(visible, m => m.FullName, paths);
        return await query.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.MissingFiles.CountAsync(cancellationToken);
    }

    /// <summary>Moves the tags of the missing file to <paramref name="target"/>, with its cached previews, and forgets it.</summary>
    private async Task RelinkAsync(SfeDbContext db, MissingFile missing, Candidate target, CancellationToken cancellationToken)
    {
        var missingId = missing.Id;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var tagIds = await db.MissingFileTags.Where(l => l.MissingFileId == missingId).Select(l => l.TagId).ToListAsync(cancellationToken);
                var existing = await db.FileTags.Where(l => l.FileId == target.Id).Select(l => l.TagId).ToListAsync(cancellationToken);
                foreach (var tagId in tagIds.Except(existing))
                    db.FileTags.Add(new FileTag { FileId = target.Id, TagId = tagId });
                await db.SaveChangesAsync(cancellationToken);
                await db.MissingFiles.Where(m => m.Id == missingId).ExecuteDeleteAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        });

        tags.InvalidateFileCounts();
        MoveCache(missing.FullName, target.FullName);
        logger.LogInformation("Relinked {Old} to {New}", missing.FullName, target.FullName);
    }

    /// <summary>
    /// Moves the thumbnail and trickplay of the old path to the new one, so they are not rendered again. Both are
    /// keyed by path and checked against the file's modification time, which a move keeps. Best effort.
    /// </summary>
    private void MoveCache(string oldPath, string newPath)
    {
        if (LibraryPaths.PathComparer.Equals(oldPath, newPath))
            return;
        try
        {
            var oldThumbnail = thumbnails.GetCachePath(oldPath);
            var newThumbnail = thumbnails.GetCachePath(newPath);
            if (File.Exists(oldThumbnail) && !File.Exists(newThumbnail))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newThumbnail)!);
                File.Move(oldThumbnail, newThumbnail);
            }

            var oldTrickplay = trickplay.GetCacheFolder(oldPath);
            var newTrickplay = trickplay.GetCacheFolder(newPath);
            if (Directory.Exists(oldTrickplay) && !Directory.Exists(newTrickplay))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newTrickplay)!);
                Directory.Move(oldTrickplay, newTrickplay);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not move the cached previews of {Old} to {New}", oldPath, newPath);
        }
    }

    /// <summary>An indexed file that may be where a missing file went.</summary>
    /// <param name="SameName">Same file name (and extension) as the missing file.</param>
    private sealed record Candidate(int Id, string FullName, string? Extension, long Size, DateTime LastWriteTime, DateTime RegisterTime, string? Sha1, bool SameName, List<int> TagIds);

    /// <summary>
    /// Indexed files of the library that may be each missing file: same size and fingerprint, or, when either has no
    /// fingerprint and <paramref name="matchByName"/> is set, same name, extension, size and modification time.
    /// </summary>
    private async Task<ILookup<int, Candidate>> CandidatesAsync(SfeDbContext db, IReadOnlyCollection<MissingFile> missing, bool matchByName, CancellationToken cancellationToken)
    {
        var sha1s = missing.Where(m => m.Sha1 is not null).Select(m => m.Sha1!).Distinct().ToList();
        var names = matchByName ? missing.Select(m => m.Name).Distinct().ToList() : [];
        if (sha1s.Count == 0 && names.Count == 0)
            return Array.Empty<Candidate>().ToLookup(_ => 0);

        var rows = await db.Files
            .AsNoTracking()
            .Where(f => (f.Sha1 != null && sha1s.Contains(f.Sha1)) || names.Contains(f.Name))
            .Select(f => new { f.Id, f.FullName, f.Name, f.Extension, f.Size, f.LastWriteTime, f.RegisterTime, f.Sha1, TagIds = f.TagLinks.Select(l => l.TagId).ToList() })
            .ToListAsync(cancellationToken);
        rows = rows.Where(r => paths.ToRelative(r.FullName) is not null).ToList();

        return missing
            .SelectMany(m => rows
                .Where(r => r.Size == m.Size && (r.Sha1 is not null && m.Sha1 is not null
                    ? string.Equals(r.Sha1, m.Sha1, StringComparison.OrdinalIgnoreCase)
                    : matchByName && SameName(r.Name, r.Extension, m) && SameTimestamp(r.LastWriteTime, m.LastWriteTime)))
                .Select(r => (m.Id, Candidate: new Candidate(r.Id, r.FullName, r.Extension, r.Size, r.LastWriteTime, r.RegisterTime, r.Sha1, SameName(r.Name, r.Extension, m), r.TagIds))))
            .ToLookup(x => x.Id, x => x.Candidate);
    }

    private static bool SameName(string name, string? extension, MissingFile missing) =>
        string.Equals(name, missing.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(extension, missing.Extension, StringComparison.OrdinalIgnoreCase);

    // Copies to another file system can round the time (FAT keeps it to 2 seconds).
    private static bool SameTimestamp(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) <= 2;
}

/// <param name="Relinked">Missing files whose tags went to their new place.</param>
/// <param name="Forgotten">Missing files dropped for being kept longer than the retention.</param>
/// <param name="Remaining">Missing files still waiting.</param>
public sealed record RelinkPassResult(int Relinked, int Forgotten, int Remaining);

/// <param name="Path">Where the file was, relative to the library (absolute when outside it).</param>
/// <param name="Candidates">Indexed files that may be it, same name first.</param>
public sealed record MissingFileInfo(
    int Id,
    string Path,
    string Name,
    long Size,
    DateTimeOffset Modified,
    DateTimeOffset MissingSince,
    IReadOnlyList<TagSummary> Tags,
    IReadOnlyList<LibraryItem> Candidates);

/// <param name="Total">Missing files in all; only on the first page.</param>
/// <param name="Next">Value of the <c>after</c> parameter that requests the next page; null on the last page.</param>
/// <param name="Enabled">Relinking is on (<c>FileTracking:Relink:Enabled</c>).</param>
public sealed record MissingFileResults(IReadOnlyList<MissingFileInfo> Items, int? Total, bool HasMore, int? Next, bool Enabled);

public enum RelinkStatus
{
    Relinked,
    NotFound,
    InvalidPath,
}
