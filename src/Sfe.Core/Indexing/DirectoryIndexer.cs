using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.FileTracking;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Search;

namespace Sfe.Core.Indexing;

/// <summary>
/// Mirrors a folder tree into the file index.
/// Folders are processed breadth-first, a bounded number at a time, each with its own DbContext.
/// </summary>
public sealed class DirectoryIndexer(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryBrowser browser,
    MediaClassifier classifier,
    MediaDimensions dimensions,
    LibraryPaths paths,
    IOptionsMonitor<SfeOptions> options,
    ILogger<DirectoryIndexer> logger)
{
    // Settings can change between (and during) scans: read them when used. The name sets are rebuilt only when the
    // lists change (a new options instance comes with new lists).
    private NameCache? _ignoredDirectories;
    private NameCache? _ignoredFiles;
    private NameCache? _excludedFolders;

    private ScanOptions Scan => options.CurrentValue.Scan;

    /// <summary>File rows written (or deleted) per database round trip.</summary>
    private const int SaveBatchSize = 1000;

    /// <summary>A folder to visit.</summary>
    /// <param name="RealPath">Physical location once symbolic links/junctions on the way are resolved.</param>
    /// <param name="LinkParents">
    /// Real paths of the folders where a link was followed on the way here. With <paramref name="RealPath"/>,
    /// every real folder above this one on the branch is one of these or one of their ancestors.
    /// </param>
    private sealed record PendingDirectory(string Path, string RealPath, string[] LinkParents);

    public async Task IndexAsync(string fullPath, bool recursive, ScanProgress progress, CancellationToken cancellationToken = default)
    {
        // "D:\Media\" and "D:\Media" are the same folder: store it the way enumeration reports it (no trailing
        // separator), otherwise its sub-folders cannot find their parent. Drive roots keep theirs.
        var start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        // A folder excluded from scans (or inside one) keeps what is indexed there as it is.
        if (FolderExclusions.Of(options.CurrentValue).IsExcluded(ExclusionKind.Scan, paths, start))
            return;
        var level = new List<PendingDirectory> { new(start, start, []) };
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Scan.Parallelism),
            CancellationToken = cancellationToken,
        };
        progress.AddQueued(level.Count);

        while (level.Count > 0)
        {
            var next = new List<PendingDirectory>();
            await Parallel.ForEachAsync(level, parallel, async (directory, ct) =>
            {
                progress.CurrentDirectory = directory.Path;
                try
                {
                    var children = await IndexDirectoryAsync(directory, progress, ct);
                    if (recursive)
                    {
                        lock (next)
                            next.AddRange(children);
                        progress.AddQueued(children.Count);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    progress.AddError();
                    logger.LogWarning(ex, "Failed to index {Directory}", directory.Path);
                }

                progress.AddQueued(-1);
                progress.AddDirectory();
            });

            level = next;
        }

        progress.CurrentDirectory = null;
    }

    /// <summary>Synchronizes one folder and its files; returns the sub-folders to visit.</summary>
    private async Task<IReadOnlyList<PendingDirectory>> IndexDirectoryAsync(PendingDirectory pending, ScanProgress progress, CancellationToken cancellationToken)
    {
        var directoryInfo = new DirectoryInfo(pending.Path);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var maxPathLength = db.PathLengthLimit;

        if (!directoryInfo.Exists)
        {
            progress.AddRemoved(await DeleteTreeAsync(db, pending.Path, cancellationToken));
            return [];
        }

        if (IsIgnoredDirectory(directoryInfo))
            return [];

        var directory = await FindOrCreateDirectoryAsync(db, directoryInfo, cancellationToken);
        // Saved first: its id is needed by the file batches, which are saved (and untracked) as they fill up.
        await db.SaveChangesAsync(cancellationToken);

        await SynchronizeFilesAsync(db, directory.Id, directoryInfo, maxPathLength, progress, cancellationToken);

        var subDirectories = new List<PendingDirectory>();
        // Folders excluded from scans: not visited, and what the index has of them stays.
        var kept = new List<string>();
        foreach (var child in directoryInfo.EnumerateDirectories("*", browser.EnumerationOptions))
        {
            if (IsExcludedFolder(child))
            {
                kept.Add(Path.TrimEndingDirectorySeparator(child.FullName));
                continue;
            }
            if (IsIgnoredDirectory(child) || !IsStorable(child.FullName, maxPathLength))
                continue;
            if (ToPending(pending, child) is { } next)
                subDirectories.Add(next);
        }

        var onDisk = subDirectories.Select(d => d.Path).Concat(kept).ToHashSet(LibraryPaths.PathComparer);
        var obsolete = await db.Directories
            .Where(d => d.ParentId == directory.Id)
            .Select(d => d.FullName)
            .ToListAsync(cancellationToken);
        foreach (var removed in obsolete.Where(d => !onDisk.Contains(d)))
            progress.AddRemoved(await DeleteTreeAsync(db, removed, cancellationToken));

        return subDirectories;
    }

    /// <summary>
    /// Adds the given files (and their folders) to the index when they are missing, without synchronizing the rest
    /// of their folders: tagging a file of a folder holding a million files must not index the whole folder.
    /// </summary>
    /// <returns>Index id of each file that is (now) indexed; ignored files and too long paths are left out.</returns>
    public async Task<Dictionary<string, int>> IndexFilesAsync(IReadOnlyCollection<string> fullPaths, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = await FileIdsAsync(db, fullPaths, cancellationToken);
        var missing = fullPaths.Where(f => !ids.ContainsKey(f)).ToList();
        if (missing.Count == 0)
            return ids;

        foreach (var folder in missing.GroupBy(f => Path.GetDirectoryName(f)!, LibraryPaths.PathComparer))
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await AddFilesAsync(db, new DirectoryInfo(folder.Key), folder.ToList(), cancellationToken);
                    break;
                }
                catch (DbUpdateException) when (attempt == 1)
                {
                    // A scan running at the same time added the folder or some of the files first: start again
                    // from its rows.
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
            }
        }

        return await FileIdsAsync(db, fullPaths, cancellationToken);
    }

    /// <summary>
    /// Reads again what the index keeps of a file whose content changed in place (an editor saved it): size, times, and
    /// its fingerprint when the scans compute them (duplicates are found by size and fingerprint). Its id, tags and
    /// everything else stay; a file the index doesn't have stays out of it.
    /// </summary>
    public async Task RefreshFileAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entry = await db.Files.FirstOrDefaultAsync(f => f.FullName == fullPath, cancellationToken);
        var file = new FileInfo(fullPath);
        if (entry is null || !file.Exists)
            return;
        await CopyAsync(file, entry, fingerprint: Scan.ComputeHashes, resolution: ReadsResolution(file), cancellationToken);
        // The old content's fingerprint would group it with its former duplicates.
        if (!Scan.ComputeHashes)
            entry.Sha1 = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddFilesAsync(SfeDbContext db, DirectoryInfo folder, List<string> files, CancellationToken cancellationToken)
    {
        var maxPathLength = db.PathLengthLimit;
        if (!folder.Exists || IsIgnoredDirectory(folder) || !IsStorable(folder.FullName, maxPathLength))
            return;

        var directory = await FindOrCreateDirectoryAsync(db, folder, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var indexed = await FileIdsAsync(db, files, cancellationToken);
        foreach (var path in files.Where(f => !indexed.ContainsKey(f)))
        {
            var file = new FileInfo(path);
            if (!file.Exists || IsIgnoredFile(file) || !IsStorable(file.FullName, maxPathLength))
                continue;
            var entry = new FileEntry { FullName = file.FullName, DirectoryId = directory.Id };
            await CopyAsync(file, entry, fingerprint: Scan.ComputeHashes, resolution: ReadsResolution(file), cancellationToken);
            db.Files.Add(entry);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, int>> FileIdsAsync(SfeDbContext db, IReadOnlyCollection<string> fullPaths, CancellationToken cancellationToken) =>
        (await db.Files
            .Where(f => fullPaths.Contains(f.FullName))
            .Select(f => new { f.FullName, f.Id })
            .ToListAsync(cancellationToken))
        .ToDictionary(f => f.FullName, f => f.Id, LibraryPaths.PathComparer);

    /// <summary>
    /// Describes a sub-folder to visit, or returns null when it is a symbolic link (or junction) leading back to a
    /// folder already being indexed on this branch, which would otherwise be followed forever.
    /// </summary>
    private PendingDirectory? ToPending(PendingDirectory parent, DirectoryInfo child)
    {
        if (child.LinkTarget is null)
            return new PendingDirectory(child.FullName, Path.Join(parent.RealPath, child.Name), parent.LinkParents);

        string target;
        try
        {
            target = Path.TrimEndingDirectorySeparator(child.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? child.FullName);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Skipping {Directory}: cannot resolve its link target", child.FullName);
            return null;
        }

        if (IsSameOrAncestor(target, parent.RealPath) || parent.LinkParents.Any(p => IsSameOrAncestor(target, p)))
        {
            logger.LogInformation("Skipping {Directory}: it links back to {Target}, which is already being indexed", child.FullName, target);
            return null;
        }

        return new PendingDirectory(child.FullName, target, [.. parent.LinkParents, parent.RealPath]);
    }

    private static bool IsSameOrAncestor(string candidate, string path) =>
        string.Equals(candidate, path, LibraryPaths.PathComparison)
        || path.StartsWith(Path.EndsInDirectorySeparator(candidate) ? candidate : candidate + Path.DirectorySeparatorChar, LibraryPaths.PathComparison);

    private static async Task<DirectoryEntry> FindOrCreateDirectoryAsync(SfeDbContext db, DirectoryInfo info, CancellationToken cancellationToken)
    {
        var fullName = info.FullName;
        var entity = await db.Directories.FirstOrDefaultAsync(d => d.FullName == fullName, cancellationToken);
        if (entity is null)
        {
            entity = new DirectoryEntry { FullName = fullName, Name = info.Name };
            db.Directories.Add(entity);
        }

        entity.CreationTime = info.CreationTime;
        entity.LastAccessTime = info.LastAccessTime;
        entity.LastWriteTime = info.LastWriteTime;

        if (entity.ParentId is null && info.Parent is not null)
        {
            entity.ParentId = await db.Directories
                .Where(d => d.FullName == info.Parent.FullName)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return entity;
    }

    private async Task SynchronizeFilesAsync(
        SfeDbContext db, int directoryId, DirectoryInfo info, int? maxPathLength, ScanProgress progress, CancellationToken cancellationToken)
    {
        // Only what the comparison needs, untracked: a folder can hold a million files.
        var indexed = await db.Files
            .AsNoTracking()
            .Where(f => f.DirectoryId == directoryId)
            .Select(f => new IndexedFile(f.Id, f.FullName, f.Size, f.LastWriteTime, f.Sha1, f.Width, f.Height, f.Duration))
            .ToDictionaryAsync(f => f.FullName, LibraryPaths.PathComparer, cancellationToken);

        var pending = 0;
        var seen = new HashSet<string>(LibraryPaths.PathComparer);
        foreach (var file in info.EnumerateFiles("*", browser.EnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsIgnoredFile(file))
                continue;

            if (!IsStorable(file.FullName, maxPathLength))
            {
                logger.LogWarning("Skipping {File}: path longer than {Max} characters", file.FullName, maxPathLength);
                continue;
            }

            seen.Add(file.FullName);
            indexed.TryGetValue(file.FullName, out var known);

            var changed = known is null || known.Size != file.Length || !SameTimestamp(known.LastWriteTime, file.LastWriteTime);
            var resolution = ReadsResolution(file);
            if (!changed && (known!.Sha1 is not null || !Scan.ComputeHashes) && (!LacksMediaProperties(file, known.Width, known.Duration) || !resolution))
                continue;

            FileEntry entry;
            if (known is null)
            {
                entry = new FileEntry { FullName = file.FullName, DirectoryId = directoryId };
                db.Files.Add(entry);
            }
            else
            {
                // Attached with its stored values, so that only the columns that change are written.
                entry = new FileEntry
                {
                    Id = known.Id, FullName = known.FullName, DirectoryId = directoryId, Size = known.Size, LastWriteTime = known.LastWriteTime,
                    Sha1 = known.Sha1, Width = known.Width, Height = known.Height, Duration = known.Duration,
                };
                db.Files.Attach(entry);
            }

            if (changed)
            {
                // The size and length of the old content no longer hold.
                entry.Width = null;
                entry.Height = null;
                entry.Duration = null;
            }

            await CopyAsync(file, entry, fingerprint: Scan.ComputeHashes && (changed || entry.Sha1 is null),
                resolution: resolution && LacksMediaProperties(file, entry.Width, entry.Duration), cancellationToken);
            progress.AddFile();

            // Saved in batches: the work done on a huge folder is kept as it goes, and the change tracker stays small.
            if (++pending >= SaveBatchSize)
            {
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
                pending = 0;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var removed = indexed.Values.Where(f => !seen.Contains(f.FullName)).Select(f => f.Id).ToList();
        foreach (var batch in removed.Chunk(SaveBatchSize))
        {
            var files = db.Files.Where(f => batch.Contains(f.Id));
            progress.AddRemoved(await MissingFileRecorder.DeleteAsync(db, files, files.ExecuteDeleteAsync, RememberMissing, cancellationToken));
        }
    }

    /// <summary>Tagged files no longer found are kept for relinking (<see cref="FileTracking.RelinkService"/>).</summary>
    private bool RememberMissing => options.CurrentValue.FileTracking.Relink.Enabled;

    private sealed record IndexedFile(int Id, string FullName, long Size, DateTime LastWriteTime, string? Sha1, int? Width, int? Height, double? Duration);

    /// <summary>
    /// The resolution and duration of this file are wanted and can be read now (videos and audio need ffmpeg).
    /// </summary>
    private bool ReadsResolution(FileInfo file) => Scan.ReadResolution && dimensions.CanRead(classifier.Classify(file.Extension));

    /// <summary>
    /// The index has not read what it keeps of this file yet: the resolution of an image, the resolution and duration
    /// of a video, the duration of an audio file. Videos and audio indexed by a version that did not read durations
    /// get theirs from the next scan.
    /// </summary>
    private bool LacksMediaProperties(FileInfo file, int? width, double? duration) => classifier.Classify(file.Extension) switch
    {
        ItemKind.Video => width is null || duration is null,
        ItemKind.Audio => duration is null,
        _ => width is null,
    };

    /// <summary>Copies the file's properties to its index entry, and its fingerprint and resolution when asked to.</summary>
    private async Task CopyAsync(FileInfo file, FileEntry entry, bool fingerprint, bool resolution, CancellationToken cancellationToken)
    {
        entry.Name = Path.GetFileNameWithoutExtension(file.Name);
        entry.Extension = file.Extension;
        entry.Size = file.Length;
        entry.CreationTime = file.CreationTime;
        entry.LastAccessTime = file.LastAccessTime;
        entry.LastWriteTime = file.LastWriteTime;
        if (fingerprint)
            entry.Sha1 = await TryFingerprintAsync(file.FullName, cancellationToken);
        if (resolution && await dimensions.ReadAsync(file.FullName, classifier.Classify(file.Extension), cancellationToken) is { } media)
        {
            entry.Width = media.Size?.Width;
            entry.Height = media.Size?.Height;
            entry.Duration = media.Duration;
        }
    }

    private async Task<string?> TryFingerprintAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            return await FileFingerprint.ComputeAsync(file, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Cannot fingerprint {File}", file);
            return null;
        }
    }

    /// <summary>
    /// Removes a folder, its sub-folders and (through cascade) their files from the index; tagged files are kept as
    /// missing files first, a whole folder moving (or its drive missing) being the case relinking matters most for.
    /// </summary>
    private async Task<int> DeleteTreeAsync(SfeDbContext db, string fullName, CancellationToken cancellationToken) =>
        await MissingFileRecorder.DeleteAsync(
            db,
            db.Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, fullName)),
            db.Directories.Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, fullName)).ExecuteDeleteAsync,
            RememberMissing,
            cancellationToken);

    /// <summary>Removes a folder and everything below it from the index (see <see cref="DeleteTreeAsync"/>).</summary>
    /// <returns>What the index held of the folder.</returns>
    public async Task<IndexedFolderStats> RemoveTreeAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        var stats = await GetStatsAsync(fullPath, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DeleteTreeAsync(db, Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath)), cancellationToken);
        return stats;
    }

    /// <summary>
    /// Forgets what the index still holds at <paramref name="fullPath"/>, a folder (with what is below it) or a file
    /// that is not on the disk any more: before a file or folder takes that name (<see cref="Files.PathMoves"/>). Tagged
    /// files are kept as missing files, as by a scan.
    /// </summary>
    internal async Task ForgetAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await MissingFileRecorder.DeleteAsync(
            db,
            db.Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, path)),
            async ct => await db.Directories.Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, path)).ExecuteDeleteAsync(ct)
                + await db.Files.Where(f => f.FullName == path).ExecuteDeleteAsync(ct),
            RememberMissing,
            cancellationToken);
    }

    /// <summary>The index row of the folder <paramref name="fullPath"/>, made (with its own parent's link) when <paramref name="create"/> and missing.</summary>
    /// <returns>null when it has none (and none was made: not asked, ignored, too long a path).</returns>
    internal async Task<int?> FolderIdAsync(SfeDbContext db, string fullPath, bool create, CancellationToken cancellationToken = default)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        var id = await db.Directories.Where(d => d.FullName == path).Select(d => (int?)d.Id).FirstOrDefaultAsync(cancellationToken);
        var folder = new DirectoryInfo(path);
        if (id is not null || !create || !folder.Exists || IsIgnoredDirectory(folder) || !IsStorable(folder.FullName, db.PathLengthLimit))
            return id;
        var entry = await FindOrCreateDirectoryAsync(db, folder, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return entry.Id;
    }

    /// <summary>What the index holds of a folder and its sub-folders.</summary>
    public async Task<IndexedFolderStats> GetStatsAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var files = db.Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, folder));
        return new IndexedFolderStats(
            await db.Directories.AnyAsync(d => d.FullName == folder, cancellationToken),
            await db.Directories.CountAsync(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, folder), cancellationToken),
            await files.CountAsync(cancellationToken),
            await files.CountAsync(f => f.TagLinks.Any(), cancellationToken));
    }

    private bool IsIgnoredDirectory(DirectoryInfo directory) =>
        NameSet(ref _ignoredDirectories, Scan.IgnoredDirectories).Contains(directory.Name)
        || IsExcludedFolder(directory)
        // SFE's work folder (the trash...) is never indexed.
        || (directory.Name.Length == LibraryPaths.WorkFolderName.Length && paths.ToRelative(directory.FullName) is { } relative
            && LibraryPaths.IsWorkFolderRelative(relative));

    /// <summary>The folder itself is on <see cref="ScanOptions.ExcludedFolders"/>.</summary>
    private bool IsExcludedFolder(DirectoryInfo directory) => ExcludedSet().Contains(Path.TrimEndingDirectorySeparator(directory.FullName));

    /// <summary>Full paths of <see cref="ScanOptions.ExcludedFolders"/>, without trailing separator.</summary>
    private HashSet<string> ExcludedSet()
    {
        var folders = Scan.ExcludedFolders;
        if (_excludedFolders is { } cached && ReferenceEquals(cached.Source, folders))
            return cached.Names;
        var set = new HashSet<string>(
            folders.Select(f => paths.TryResolve(f, out var full) ? Path.TrimEndingDirectorySeparator(full) : null).OfType<string>(),
            LibraryPaths.PathComparer);
        _excludedFolders = new NameCache(folders, set);
        return set;
    }

    private bool IsIgnoredFile(FileInfo file) => NameSet(ref _ignoredFiles, Scan.IgnoredFiles).Contains(file.Name);

    /// <summary>A class, so that parallel scan tasks swap it with one atomic reference write.</summary>
    private sealed record NameCache(List<string> Source, HashSet<string> Names);

    private static HashSet<string> NameSet(ref NameCache? cache, List<string> names)
    {
        if (cache is { } cached && ReferenceEquals(cached.Source, names))
            return cached.Names;
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        cache = new NameCache(names, set);
        return set;
    }

    private static bool IsStorable(string fullName, int? maxPathLength) => maxPathLength is null || fullName.Length <= maxPathLength;

    // SQL Server DATETIME has a ~3 ms resolution: a sub-second difference is a rounding artifact, not a change.
    private static bool SameTimestamp(DateTime stored, DateTime actual) => Math.Abs((stored - actual).TotalSeconds) < 1;
}
