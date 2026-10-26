using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Files.Compression;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Search;
using Sfe.Core.Tagging;
using Sfe.Core.Tasks;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Core.Files;

public enum FileJobKind
{
    Move,
    Copy,

    /// <summary>An archive's entries written into a folder (agents/docs/features/archive-extraction.md).</summary>
    Extract,

    /// <summary>Files and folders written into one zip in a folder (agents/docs/features/compression.md).</summary>
    Compress,
}

public enum FileJobState
{
    Running,

    /// <summary>Waits for the person's answer to a name clash (<see cref="FileJobView.Conflict"/>).</summary>
    Waiting,

    Completed,
    Cancelled,
}

/// <summary>What to do when the target folder already holds an item of the same name.</summary>
public enum ConflictChoice
{
    /// <summary>Wait for the person's answer (<see cref="FileJobs.Answer"/>).</summary>
    Ask,

    /// <summary>The item there goes to the trash (needs the right to delete there), this one takes its place.</summary>
    Replace,

    Skip,

    /// <summary>This one gets a free name: <c>name (2).ext</c>.</summary>
    KeepBoth,
}

/// <param name="Paths">Files and folders to move or copy (root-relative); the archive to extract.</param>
/// <param name="Target">The folder they go into.</param>
/// <param name="KeepAccess">Moved folders keep the access each group and account had (folder rules pinning it).</param>
/// <param name="KeepTags">Copies get the tags of their originals.</param>
/// <param name="Entries">Extract: the paths inside the archive to extract (each with what is below it); null: all of it.</param>
/// <param name="NewFolder">
/// Extract: into a new folder named after the archive (or after its one top folder), rather than straight into the target.
/// </param>
/// <param name="Name">Compress: the zip's name (<c>.zip</c> added); null: its only item's, else their folder's.</param>
/// <param name="Level">Compress: the deflate level, 0 (stored) to 9.</param>
/// <param name="SkipCompressed">Compress: files already compressed (pictures, videos, archives…) stored whatever the level.</param>
public sealed record FileJobRequest(FileJobKind Kind, IReadOnlyList<string> Paths, string Target, ConflictChoice Conflicts = ConflictChoice.Ask,
    bool KeepAccess = false, bool KeepTags = false, IReadOnlyList<string>? Entries = null, bool NewFolder = false,
    string? Name = null, int Level = 6, bool SkipCompressed = true);

/// <summary>An item on one side of a name clash.</summary>
public sealed record ClashSide(string Name, bool IsFolder, long? Size, DateTimeOffset? Modified);

/// <param name="Source">What is moved or copied.</param>
/// <param name="Existing">What is already there under that name.</param>
/// <param name="CanReplace">The person may replace it (it would go to the trash).</param>
public sealed record FileJobConflict(ClashSide Source, ClashSide Existing, bool CanReplace);

/// <param name="NewPath">Where it is now (moved, or the copy); null when not done.</param>
public sealed record FileJobItem(string Path, string? NewPath, FileOperationStatus Status, string? Message = null);

/// <summary>A move or copy as the operations panel shows it.</summary>
public sealed record FileJobView(
    int Id, FileJobKind Kind, FileJobState State, string Target, int Total, int Done, long BytesTotal, long BytesDone, string? Current,
    FileJobConflict? Conflict, IReadOnlyList<FileJobItem> Results, DateTimeOffset Started, DateTimeOffset? Finished, int? OwnerId);

/// <summary>
/// What the person who started a job may do, asked again for every item as it runs (rights and folder rules can change
/// meanwhile): the routes implement it with the person's current access.
/// </summary>
public interface IFileJobAccess
{
    /// <summary>The person may use <paramref name="right"/> at the root-relative <paramref name="path"/>.</summary>
    Task<bool> MayAsync(string path, string right);

    /// <summary>The person may use <paramref name="right"/> at the folder <paramref name="path"/> and everywhere below it.</summary>
    Task<bool> MayThroughoutAsync(string path, string right);

    /// <summary>The full path really is (through links) among the server's own files or a work folder: never written to.</summary>
    bool IsProtected(string fullPath);

    /// <summary>
    /// The person's access now, for a walk through folders (a compression: <c>files.view</c> and <c>files.download</c> at
    /// each); null when they have none any more.
    /// </summary>
    Task<IArchiveAccess?> ArchiveAccessAsync() => Task.FromResult<IArchiveAccess?>(null);
}

/// <summary>
/// Moves and copies (agents/docs/features/move-and-copy.md): each request is a background job with progress, cancel,
/// questions on name clashes and a final report, re-checking the person's rights at every item. Moves keep what SFE
/// knows by path (<see cref="PathMoves"/>, folder rules); copies never follow links, leave out what the person may not
/// see, leave no partial file when cancelled, and are indexed (a scan of a copied folder).
/// </summary>
public sealed class FileJobs(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    WorkFolder work,
    DirectoryIndexer indexer,
    LibraryBrowser browser,
    TagService tags,
    ThumbnailService thumbnails,
    TrickplayService trickplay,
    ScanCoordinator scans,
    PathMoves moves,
    Trash trash,
    TenantBinding tenant,
    IOptionsMonitor<SfeOptions> options,
    ILogger<FileJobs> logger,
    FolderRuleService? rules = null,
    Archives? archives = null,
    ArchiveBuildGate? gates = null) : IDisposable
{
    /// <summary>Finished jobs kept for the operations panel.</summary>
    private const int KeptJobs = 50;
    private const int CopyBuffer = 1 << 20;

    /// <summary>Bytes written between two reads of the free space while extracting.</summary>
    private const long FreeSpaceStep = 64L << 20;

    private readonly ConcurrentDictionary<int, Job> _jobs = new();
    private readonly CancellationTokenSource _shutdown = new();
    private int _nextId;

    /// <summary>Folders an extraction writes into before they take their names: never a name of the person's.</summary>
    public const string PartialSuffix = ".sfe-partial";

    /// <summary>
    /// Starts a move, copy or extraction; the result is its first view. <paramref name="password"/>: the archive's, held
    /// by the job only while it runs (never in its view, its request or the log).
    /// </summary>
    public FileJobView Start(FileJobRequest request, FileActor actor, IFileJobAccess access, string? password = null)
    {
        var job = new Job(Interlocked.Increment(ref _nextId), request, actor, access, CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
        {
            Password = request.Kind is FileJobKind.Extract or FileJobKind.Compress && !string.IsNullOrEmpty(password) ? password : null,
        };
        _jobs[job.Id] = job;
        job.Completion = Task.Run(() => RunAsync(job), CancellationToken.None);
        Prune();
        return job.View();
    }

    /// <summary>The jobs of <paramref name="ownerId"/> (all with null), newest first.</summary>
    public IReadOnlyList<FileJobView> List(int? ownerId) =>
        [.. _jobs.Values.Where(j => ownerId is null || j.Actor.AccountId == ownerId).OrderByDescending(j => j.Id).Select(j => j.View())];

    public FileJobView? Find(int id) => _jobs.TryGetValue(id, out var job) ? job.View() : null;

    /// <summary>The job's task; for tests.</summary>
    internal Task? Completion(int id) => _jobs.TryGetValue(id, out var job) ? job.Completion : null;

    /// <summary>Answers the job's question about a name clash; <paramref name="forAll"/>: the same for the next ones.</summary>
    public bool Answer(int id, ConflictChoice choice, bool forAll) =>
        choice != ConflictChoice.Ask && _jobs.TryGetValue(id, out var job) && job.Answer(choice, forAll);

    public bool Cancel(int id)
    {
        if (!_jobs.TryGetValue(id, out var job) || job.State is FileJobState.Completed or FileJobState.Cancelled)
            return false;
        job.Cancellation.Cancel();
        return true;
    }

    private async Task RunAsync(Job job)
    {
        var request = job.Request;
        var token = job.Cancellation.Token;
        var copiedFolders = new List<string>();
        try
        {
            if (LibraryPaths.NormalizeRelative(request.Target) is not { } targetPath || !paths.TryResolve(targetPath, out var targetFull)
                || !Directory.Exists(targetFull) || job.Access.IsProtected(targetFull))
            {
                foreach (var path in request.Paths)
                    job.Report(new FileJobItem(path, null, FileOperationStatus.NotFound, "The folder they would go to was not found."));
                return;
            }
            targetPath = paths.ToRelative(targetFull)!;
            if (request.Kind is FileJobKind.Copy or FileJobKind.Extract or FileJobKind.Compress)
                job.BytesTotal = request.Paths.Sum(path => paths.TryResolve(path, out var full) ? SizeOf(full) : 0);
            if (request.Kind == FileJobKind.Compress)
            {
                FileJobItem made;
                try
                {
                    made = await CompressAsync(job, targetPath, targetFull, token);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "{Account} could not compress into {Target}", job.Actor.Name, targetPath);
                    made = new FileJobItem(request.Paths.Count == 1 ? request.Paths[0] : targetPath, null, FileOperationStatus.Failed,
                        ex is NoRoomException ? ex.Message : FileOperations.DiskFailure(ex, "The zip could not be written").Message);
                }
                job.Report(made);
                job.Finish(FileJobState.Completed);
                return;
            }

            foreach (var path in request.Paths.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                job.Current = path;
                FileJobItem result;
                try
                {
                    result = request.Kind == FileJobKind.Extract
                        ? await ExtractAsync(job, path, targetPath, targetFull, copiedFolders, token)
                        : await ItemAsync(job, path, targetPath, targetFull, copiedFolders, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "{Account} could not {Kind} {Path} to {Target}", job.Actor.Name, request.Kind.ToString().ToLowerInvariant(), path, targetPath);
                    result = new FileJobItem(path, null, FileOperationStatus.Failed,
                        ex is LinksInsideException or NoRoomException ? ex.Message
                        : FileOperations.DiskFailure(ex, $"“{Base(path)}” could not be {request.Kind switch { FileJobKind.Move => "moved", FileJobKind.Copy => "copied", _ => "extracted" }}").Message);
                }
                job.Report(result);
            }
            job.Finish(FileJobState.Completed);
        }
        catch (OperationCanceledException)
        {
            job.Finish(FileJobState.Cancelled);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The {Kind} job {Id} failed", request.Kind, job.Id);
            job.Finish(FileJobState.Completed);
        }
        finally
        {
            job.Password = null;
            browser.Invalidate(paths.TryResolve(request.Target, out var target) ? target : paths.Root);
            tags.InvalidateFileCounts();
            // Copied folders are indexed by a scan of each (none starts while another runs: the next scan does it).
            foreach (var folder in copiedFolders)
                scans.TryStart(folder, TaskTrigger.Automatic);
        }
    }

    private async Task<FileJobItem> ItemAsync(Job job, string path, string targetPath, string targetFull, List<string> copiedFolders, CancellationToken token)
    {
        var request = job.Request;
        var move = request.Kind == FileJobKind.Move;
        var right = move ? Rights.MoveFiles : Rights.CopyFiles;
        if (LibraryPaths.NormalizeRelative(path) is not { Length: > 0 } || !paths.TryResolve(path, out var source)
            || paths.ToRelative(source) is not { Length: > 0 } sourcePath || (!File.Exists(source) && !Directory.Exists(source))
            || job.Access.IsProtected(source) || !await job.Access.MayAsync(sourcePath, Rights.Browse))
            return new FileJobItem(path, null, FileOperationStatus.NotFound, "It was not found.");
        var isFolder = Directory.Exists(source);
        var name = Path.GetFileName(source);

        // Who may: moves change the source's folder and the target, copies open the source (a folder: its files; the
        // folders below that may not be opened are left out) and change the target.
        var allowed = move
            ? await job.Access.MayAsync(sourcePath, right) && await job.Access.MayAsync(Parent(sourcePath), right)
                && (!isFolder || await job.Access.MayThroughoutAsync(sourcePath, right))
            : await job.Access.MayAsync(sourcePath, Rights.ViewFiles);
        if (!allowed || !await job.Access.MayAsync(targetPath, right))
            return new FileJobItem(path, null, FileOperationStatus.Forbidden, $"You may not {(move ? "move" : "copy")} “{name}” there.");
        if (isFolder && FolderPolicy.IsSameOrBelow(targetPath, sourcePath))
            return new FileJobItem(path, null, FileOperationStatus.Failed, $"“{name}” can't go into itself.");
        if (move && LibraryPaths.PathComparer.Equals(Parent(sourcePath), targetPath))
            return new FileJobItem(path, sourcePath, FileOperationStatus.Skipped, $"“{name}” is already there.");

        // A name clash: the person's choice, asked when needed.
        var (clash, refused) = await ClashAsync(job, path, source, sourcePath, isFolder, targetPath, targetFull, name,
            request.Kind == FileJobKind.Copy && LibraryPaths.PathComparer.Equals(Parent(sourcePath), targetPath), token);
        if (refused is not null)
            return refused;
        name = clash!;
        var destination = Path.Combine(targetFull, name);
        var destinationPath = Join(targetPath, name);
        if (await TooLongForTheIndexAsync(source, destination.Length - source.Length, token) is { } tooLong)
            return new FileJobItem(path, null, FileOperationStatus.Invalid, tooLong);

        return move
            ? await MoveAsync(job, path, source, sourcePath, destination, destinationPath, isFolder, token)
            : await CopyAsync(job, path, source, sourcePath, destination, destinationPath, isFolder, copiedFolders, token);
    }

    /// <summary>
    /// The name <paramref name="name"/> takes in the target: itself when free, else the person's choice (asked when
    /// needed): another name, or the item there replaced (to the trash); or the item's result when it is left out.
    /// </summary>
    /// <param name="sourcePath">The item's place in the library, if it has one (an extracted item has none).</param>
    /// <param name="keepBoth">Keep both without asking (a copy in its own folder).</param>
    private async Task<(string? Name, FileJobItem? Refused)> ClashAsync(Job job, string path, string source, string? sourcePath, bool isFolder,
        string targetPath, string targetFull, string name, bool keepBoth, CancellationToken token)
    {
        // A name no route reaches (the work folder at the root, its 8.3 name): never placed, never replaced.
        if (Reserved(targetPath, name))
            return (null, new FileJobItem(path, null, FileOperationStatus.Invalid, $"“{name}” was left out: that name is kept for SFE's own files."));
        var destination = Path.Combine(targetFull, name);
        if (!File.Exists(destination) && !Directory.Exists(destination))
            return (name, null);
        var replaceRight = trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles;
        var existingPath = Join(targetPath, name);
        // Never what holds the item itself (moving X/X next to X, then replacing X).
        var canReplace = (sourcePath is null || !FolderPolicy.IsSameOrBelow(sourcePath, existingPath))
            && await job.Access.MayAsync(existingPath, replaceRight) && await job.Access.MayAsync(targetPath, replaceRight)
            && (!Directory.Exists(destination) || await job.Access.MayThroughoutAsync(existingPath, replaceRight));
        var choice = keepBoth ? ConflictChoice.KeepBoth : await job.ChooseAsync(new FileJobConflict(Side(source), Side(destination), canReplace), token);
        switch (choice)
        {
            case ConflictChoice.Skip:
                return (null, new FileJobItem(path, null, FileOperationStatus.Skipped, $"“{name}” was left out: there is one of that name there."));
            case ConflictChoice.KeepBoth:
                return (FileNames.KeepBoth(name, isFolder, n => File.Exists(Path.Combine(targetFull, n)) || Directory.Exists(Path.Combine(targetFull, n)))
                    ?? throw new IOException("No free name."), null);
            case ConflictChoice.Replace when canReplace:
                using (var hold = scans.TryBeginOperation(destination))
                {
                    if (hold is null)
                        return (null, ScanRunning(path));
                    if (trash.AlwaysPermanent)
                        await trash.DeleteForGoodAsync(destination, existingPath, Directory.Exists(destination), job.Actor, token);
                    else
                        await trash.MoveInAsync(destination, existingPath, Directory.Exists(destination), job.Actor, token);
                }
                return (name, null);
            default:
                return (null, new FileJobItem(path, null, FileOperationStatus.Forbidden, $"You may not replace “{name}” there."));
        }
    }

    private async Task<FileJobItem> MoveAsync(Job job, string path, string source, string sourcePath, string destination, string destinationPath, bool isFolder,
        CancellationToken token)
    {
        using var hold = scans.TryBeginOperation(source, destination);
        if (hold is null)
            return ScanRunning(path);

        FolderRuleMove? ruleMove = null;
        if (isFolder && rules is not null && tenant.TenantId is { } tenantId)
        {
            var begun = await rules.BeginMoveAsync(tenantId, sourcePath, destinationPath, job.Request.KeepAccess, job.Actor.AccountId, token);
            if (begun.Status != AccessStatus.Ok)
                return new FileJobItem(path, null, FileOperationStatus.Conflict, begun.Problem is not null
                    ? $"“{Base(destinationPath)}” can't go there: folder access rules are set for a folder of that name. An administrator can remove them or point them elsewhere."
                    : begun.Errors?.Values.SelectMany(e => e).FirstOrDefault());
            ruleMove = begun.Value;
        }

        await using (ruleMove)
        {
            try
            {
                FileTree.Move(source, destination, isFolder);
            }
            catch
            {
                if (ruleMove is not null)
                    await ruleMove.AbortAsync(CancellationToken.None);
                throw;
            }
            // The disk has changed: what follows is not stopped by a cancel.
            await moves.MoveAsync(source, destination, isFolder, CancellationToken.None);
            if (ruleMove is not null)
                await ruleMove.CompleteAsync(CancellationToken.None);
        }
        logger.LogInformation("{Account} moved {Path} to {Target}", job.Actor.Name, sourcePath, destinationPath);
        return new FileJobItem(path, destinationPath, FileOperationStatus.Done);
    }

    private async Task<FileJobItem> CopyAsync(Job job, string path, string source, string sourcePath, string destination, string destinationPath, bool isFolder,
        List<string> copiedFolders, CancellationToken token)
    {
        var needed = SizeOf(source);
        var margin = Math.Max(0, options.CurrentValue.Files.MinimumFreeSpaceMegabytes) * 1024L * 1024;
        if (FreeSpace(Path.GetDirectoryName(destination)!) is { } free && free - needed < margin)
            return new FileJobItem(path, null, FileOperationStatus.Failed,
                $"“{Base(path)}” was not copied: the drive would keep less than {margin / 1024 / 1024:N0} MB free.");

        using var hold = scans.TryBeginOperation(destination);
        if (hold is null)
            return ScanRunning(path);

        var skipped = new List<string>();
        try
        {
            if (isFolder)
                await CopyFolderAsync(job, source, sourcePath, destination, skipped, token);
            else
                await CopyFileAsync(job, source, destination, token);
        }
        catch
        {
            // Cancelled or failed: nothing half copied stays.
            if (Directory.Exists(destination))
                FileTree.Delete(destination);
            else if (File.Exists(destination))
                File.Delete(destination);
            throw;
        }

        // The copies: indexed (a file now, a folder by a scan), with the previews and, when asked, the tags of the originals.
        try
        {
            await AfterCopyAsync(job, source, destination, isFolder, copiedFolders, destinationPath, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The copy of {Path} could not be indexed: the next scan does", sourcePath);
        }
        logger.LogInformation("{Account} copied {Path} to {Target}", job.Actor.Name, sourcePath, destinationPath);
        return new FileJobItem(path, destinationPath, FileOperationStatus.Done, skipped.Count == 0 ? null
            : $"Left out: {string.Join(", ", skipped.Take(5))}{(skipped.Count > 5 ? $" and {skipped.Count - 5} more" : string.Empty)} (links, or folders you may not see).");
    }

    private async Task CopyFolderAsync(Job job, string source, string sourcePath, string destination, List<string> skipped, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            token.ThrowIfCancellationRequested();
            var entryPath = Join(sourcePath, entry.Name);
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
                await CopyFolderAsync(job, entry.FullName, entryPath, Path.Combine(destination, entry.Name), skipped, token);
            }
            else
            {
                await CopyFileAsync(job, entry.FullName, Path.Combine(destination, entry.Name), token);
            }
        }
    }

    private static async Task CopyFileAsync(Job job, string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBuffer, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBuffer, FileOptions.Asynchronous);
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

    private async Task AfterCopyAsync(Job job, string source, string destination, bool isFolder, List<string> copiedFolders, string destinationPath, CancellationToken token)
    {
        browser.Invalidate(Path.GetDirectoryName(destination)!);
        await using var db = await dbFactory.CreateDbContextAsync(token);
        // Previews and tags come from the originals' index rows (at most what was indexed).
        var originals = await db.Files.AsNoTracking()
            .Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, source))
            .Select(f => new { f.Id, f.FullName })
            .ToListAsync(token);
        foreach (var original in originals)
            CopyCache(original.FullName, destination + original.FullName[source.Length..]);

        if (job.Request.KeepTags)
        {
            var ids = originals.Select(o => o.Id).ToList();
            var tagged = (await db.FileTags.AsNoTracking().Where(l => ids.Contains(l.FileId)).Select(l => new { l.FileId, l.TagId }).ToListAsync(token))
                .GroupBy(l => l.FileId)
                .ToDictionary(g => destination + originals.First(o => o.Id == g.Key).FullName[source.Length..], g => g.Select(l => l.TagId).ToList(), LibraryPaths.PathComparer);
            if (tagged.Count > 0)
            {
                var copies = await indexer.IndexFilesAsync([.. tagged.Keys], token);
                // Indexed, the copies may have the default tags of their folder already.
                var copyIds = copies.Values.ToList();
                var existing = (await db.FileTags.AsNoTracking().Where(l => copyIds.Contains(l.FileId)).Select(l => new { l.FileId, l.TagId }).ToListAsync(token))
                    .Select(l => (l.FileId, l.TagId))
                    .ToHashSet();
                foreach (var (file, tagIds) in tagged)
                {
                    if (copies.TryGetValue(file, out var fileId))
                        foreach (var tagId in tagIds.Where(t => !existing.Contains((fileId, t))))
                            db.FileTags.Add(new FileTag { FileId = fileId, TagId = tagId });
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
    /// Extracts the archive <paramref name="path"/> (its entries chosen, or all) into a partial folder of the target,
    /// then gives what came out its place: the new folder (named after the archive, or after its one top folder), or
    /// each top item in the target with the clash questions. Cancelled or failed (a wrong password, the disk full):
    /// the partial folder goes, nothing half written stays.
    /// </summary>
    private async Task<FileJobItem> ExtractAsync(Job job, string path, string targetPath, string targetFull, List<string> extractedFolders, CancellationToken token)
    {
        if (LibraryPaths.NormalizeRelative(path) is not { Length: > 0 } || !paths.TryResolve(path, out var source)
            || paths.ToRelative(source) is not { Length: > 0 } sourcePath || !File.Exists(source)
            || job.Access.IsProtected(source) || !await job.Access.MayAsync(sourcePath, Rights.Browse))
            return new FileJobItem(path, null, FileOperationStatus.NotFound, "It was not found.");
        var name = Path.GetFileName(source);
        // An archive's content is the file's content: it is opened (view, download) and written where one may extract.
        if (!await job.Access.MayAsync(sourcePath, Rights.ViewFiles) || !await job.Access.MayAsync(sourcePath, Rights.Download)
            || !await job.Access.MayAsync(targetPath, Rights.ExtractFiles))
            return new FileJobItem(path, null, FileOperationStatus.Forbidden, $"You may not extract “{name}” there.");
        if (ArchiveFormats.Detect(source) is not { } format)
            return new FileJobItem(path, null, FileOperationStatus.Invalid, $"“{name}” is not an archive SFE extracts (zip, 7z, rar, tar, gz, bz2, xz).");

        var settings = options.CurrentValue;
        var stem = StemOf(name);
        RemoveLeftovers(job, targetFull);
        var partial = PartialFolder(targetFull, stem);
        using var hold = scans.TryBeginOperation(partial);
        if (hold is null)
            return ScanRunning(path);
        job.Partials.Add(partial);

        ExtractResult extracted;
        try
        {
            Directory.CreateDirectory(partial);
            int? maxFullPath = null;
            await using (var db = await dbFactory.CreateDbContextAsync(token))
                maxFullPath = db.PathLengthLimit;
            var archives = settings.Files.Archives;
            var limits = new ExtractLimits(archives.MaxDepth, archives.MaxEntries, archives.MaxCompressionRatio, settings.Files.PortableNames, maxFullPath);
            var margin = Math.Max(0, settings.Files.MinimumFreeSpaceMegabytes) * 1024L * 1024;
            var unchecked_ = FreeSpaceStep;   // the first file: read at once
            extracted = ArchiveExtractor.Extract(source, format, job.Password, partial, job.Request.Entries, limits,
                reserve: size =>
                {
                    // The free space, read before each big file and every 64 MB of small ones.
                    unchecked_ += size;
                    if (unchecked_ < FreeSpaceStep && size < FreeSpaceStep)
                        return;
                    unchecked_ = 0;
                    if (FreeSpace(partial) is { } free && free - size < margin)
                        throw new NoRoomException($"“{name}” was not extracted: the drive would keep less than {margin / 1024 / 1024:N0} MB free.");
                },
                read: job.SetBytes, cancel: token);
        }
        catch (Exception ex)
        {
            RemovePartial(partial);
            if (ex is ArchiveException { NeedsPassword: true })
                return new FileJobItem(path, null, FileOperationStatus.Failed, string.IsNullOrEmpty(job.Password)
                    ? $"“{name}” is protected by a password: extract it with its password."
                    : $"That password doesn't open “{name}”: nothing was extracted.");
            if (ex is ArchiveException)
                return new FileJobItem(path, null, FileOperationStatus.Failed, $"“{name}” could not be extracted: {Lower(ex.Message)}");
            throw;
        }

        // What came out takes its place (the files are whole); a cancel while a clash is asked leaves the rest out.
        var placed = new List<(string Full, bool IsFolder)>();
        var skipped = 0;
        string? newPath = targetPath;
        try
        {
            var top = Directory.EnumerateFileSystemEntries(partial).Take(2).ToList();
            if (top.Count == 0)
            {
                newPath = null;
            }
            else if (job.Request.NewFolder && format != ArchiveFormat.Single)
            {
                // One folder at the top keeps its name ("trip.zip" holding "trip/" gives "trip", not "trip/trip").
                var single = top.Count == 1 && Directory.Exists(top[0]) ? top[0] : null;
                var folderName = single is null ? stem : Path.GetFileName(single);
                var taken = (string n) => Taken(targetFull)(n) || Reserved(targetPath, n);
                if (taken(folderName))
                    folderName = FileNames.KeepBoth(folderName, true, taken) ?? throw new IOException("No free name.");
                var destination = Path.Combine(targetFull, folderName);
                using var place = scans.TryBeginOperation(destination);
                if (place is null)
                {
                    RemovePartial(partial);
                    return ScanRunning(path);
                }
                Directory.Move(single ?? partial, destination);
                placed.Add((destination, true));
                newPath = Join(targetPath, folderName);
            }
            else
            {
                foreach (var item in Directory.EnumerateFileSystemEntries(partial).ToList())
                {
                    var isFolder = Directory.Exists(item);
                    var (itemName, refused) = await ClashAsync(job, path, item, null, isFolder, targetPath, targetFull, Path.GetFileName(item), false, token);
                    if (refused is not null)
                    {
                        skipped++;
                        continue;
                    }
                    var destination = Path.Combine(targetFull, itemName!);
                    using var place = scans.TryBeginOperation(destination);
                    if (place is null)
                    {
                        skipped++;
                        continue;
                    }
                    FileTree.Move(item, destination, isFolder);
                    placed.Add((destination, isFolder));
                }
            }
        }
        finally
        {
            RemovePartial(partial);
        }

        try
        {
            browser.Invalidate(targetFull);
            var files = placed.Where(p => !p.IsFolder).Select(p => p.Full).ToList();
            if (files.Count > 0)
                await indexer.IndexFilesAsync(files, CancellationToken.None);
            // Folders by a scan: of the one folder, or of the target holding several (one scan runs at a time).
            var folders = placed.Where(p => p.IsFolder).Select(p => paths.ToRelative(p.Full)!).ToList();
            if (folders.Count == 1)
                extractedFolders.Add(folders[0]);
            else if (folders.Count > 1)
                extractedFolders.Add(targetPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "What was extracted from {Path} could not be indexed: the next scan does", sourcePath);
        }
        logger.LogInformation("{Account} extracted {Path} to {Target}: {Files} files, {Folders} folders, {LeftOut} left out",
            job.Actor.Name, sourcePath, newPath ?? targetPath, extracted.Files, extracted.Folders, extracted.LeftOutCount);
        return new FileJobItem(path, newPath, FileOperationStatus.Done, ExtractMessage(extracted, skipped));
    }

    /// <summary>
    /// Compresses the request's items into one zip in the target (agents/docs/features/compression.md): the walk of the
    /// downloads (<see cref="Archives"/>: links never followed, what the person may not see left out) into SFE's writer
    /// (the level asked, a password), one compression at a time on the server. Written into a partial file, then given its
    /// name (a clash asked then); cancelled or failed, the partial file goes.
    /// </summary>
    private async Task<FileJobItem> CompressAsync(Job job, string targetPath, string targetFull, CancellationToken token)
    {
        var request = job.Request;
        var label = request.Paths.Count == 1 ? request.Paths[0] : targetPath;
        if (archives is null || gates is null)
            return new FileJobItem(label, null, FileOperationStatus.Failed, "Compressing is not available on this server.");
        if (!await job.Access.MayAsync(targetPath, Rights.CompressFiles) || await job.Access.ArchiveAccessAsync() is not { } access)
            return new FileJobItem(label, null, FileOperationStatus.Forbidden, "You may not compress into this folder.");
        var plan = archives.Plan(request.Paths.Distinct(StringComparer.Ordinal).ToList(), access);
        if (plan.Plan is not { } chosen)
            return new FileJobItem(label, null, plan.Status, plan.Message);

        var name = string.IsNullOrWhiteSpace(request.Name) ? chosen.FileName : request.Name.Trim();
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            name += ".zip";
        var settings = options.CurrentValue;
        if (FileNames.Check(name, settings.Files.PortableNames) is { } refused)
            return new FileJobItem(label, null, FileOperationStatus.Invalid, refused);
        var level = Math.Clamp(request.Level, 0, 9);

        // The index's sizes: an upper bound of the zip (files already compressed are stored, the rest shrinks).
        var margin = Math.Max(0, settings.Files.MinimumFreeSpaceMegabytes) * 1024L * 1024;
        var bound = await archives.IndexedSizeAsync(chosen, token);
        if (FreeSpace(targetFull) is { } free && free - bound < margin)
            return new FileJobItem(label, null, FileOperationStatus.Failed,
                $"“{name}” was not made: the drive would keep less than {margin / 1024 / 1024:N0} MB free.");

        // One compression at a time on the server: the others wait their turn (the panel shows them running).
        var gate = gates.Compressions(settings.Files.Archives.CompressJobs);
        await gate.WaitAsync(token);
        try
        {
            RemoveLeftovers(job, targetFull);
            var partial = PartialFolder(targetFull, name);
            using var hold = scans.TryBeginOperation(partial);
            if (hold is null)
                return ScanRunning(label);
            job.Partials.Add(partial);
            ArchiveSummary summary;
            long zipLength;
            try
            {
                await using var file = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, CopyBuffer);
                await using var writer = new ZipWriter(file, job.Password);
                var checkedAt = 0L;
                summary = await archives.CompressAsync(chosen, writer, new CompressOptions(level, request.SkipCompressed, LibraryPaths.RealPath(partial)), access,
                    read: count => job.AddBytes(count),
                    written: length =>
                    {
                        // The free space, read every 64 MB written.
                        if (length - checkedAt < FreeSpaceStep)
                            return;
                        checkedAt = length;
                        if (FreeSpace(targetFull) is { } left && left < margin)
                            throw new NoRoomException($"“{name}” was not made: the drive would keep less than {margin / 1024 / 1024:N0} MB free.");
                    },
                    cancellationToken: token);
                writer.Finish();
                zipLength = file.Length;
            }
            catch
            {
                RemovePartialFile(partial);
                throw;
            }

            // Its name, now that it is whole: a clash is asked (or answered by the request).
            var (finalName, clash) = await ClashAsync(job, label, partial, null, false, targetPath, targetFull, name, false, token);
            if (clash is not null)
            {
                RemovePartialFile(partial);
                return clash;
            }
            var destination = Path.Combine(targetFull, finalName!);
            using (var place = scans.TryBeginOperation(destination))
            {
                if (place is null)
                {
                    RemovePartialFile(partial);
                    return ScanRunning(label);
                }
                File.Move(partial, destination);
            }
            try
            {
                browser.Invalidate(targetFull);
                await indexer.IndexFilesAsync([destination], CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The zip {Path} could not be indexed: the next scan does", destination);
            }
            var newPath = Join(targetPath, finalName!);
            logger.LogInformation("{Account} compressed {Count} items into {Path}: {Files} files, {Bytes} bytes into {Zip} (level {Level}{Encrypted})",
                job.Actor.Name, chosen.Items.Count, newPath, summary.Files, summary.Bytes, zipLength, level, job.Password is null ? "" : ", encrypted");
            return new FileJobItem(label, newPath, FileOperationStatus.Done, CompressMessage(summary, zipLength, job.Password is not null));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>What a compression says when it is done: what went in, the ratio, what was left out.</summary>
    private static string CompressMessage(ArchiveSummary summary, long zipLength, bool encrypted)
    {
        var ratio = summary.Bytes > 0 ? $" ({zipLength * 100.0 / summary.Bytes:0} %)" : string.Empty;
        var text = $"{Count(summary.Files, "file")}: {FormatBytes(summary.Bytes)} → {FormatBytes(zipLength)}{ratio}{(encrypted ? ", encrypted" : string.Empty)}.";
        if (summary.LeftOut.Count > 0)
            text += $" Left out {Count(summary.LeftOut.Count, "item")} (listed in “{Archives.LeftOutEntry}”).";
        return text;
    }

    private void RemovePartialFile(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove the partial file {File}: the next compression there does", partial);
        }
    }

    /// <summary>What an extraction says when it is done: what it wrote, what it left out and why, where it stopped.</summary>
    private static string ExtractMessage(ExtractResult result, int skipped)
    {
        var parts = new List<string>
        {
            result.Files + result.Folders == 0
                ? "Nothing was extracted."
                : $"{Count(result.Files, "file")} ({FormatBytes(result.Bytes)}).",
        };
        if (result.LeftOutCount > 0)
        {
            var named = string.Join("; ", result.LeftOut.Take(5).Select(l => $"“{l.Name}”: {l.Reason}"));
            parts.Add($"Left out {Count(result.LeftOutCount, "entry", "entries")}: {named}{(result.LeftOutCount > 5 ? $"; and {result.LeftOutCount - 5:N0} more" : string.Empty)}.");
        }
        if (skipped > 0)
            parts.Add($"{Count(skipped, "item")} not placed: there is one of that name in the folder.");
        if (result.Stopped is { } stopped)
            parts.Add(stopped);
        return string.Join(' ', parts);
    }

    private static string Count(long count, string one, string? many = null) => count == 1 ? $"1 {one}" : $"{count:N0} {many ?? one + "s"}";

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };

    private static string Lower(string message) => message.Length > 0 ? char.ToLowerInvariant(message[0]) + message[1..] : message;

    /// <summary>The archive's name without what makes it an archive: <c>trip.tar.gz</c> → <c>trip</c>.</summary>
    internal static string StemOf(string name)
    {
        foreach (var double_ in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
            if (name.EndsWith(double_, StringComparison.OrdinalIgnoreCase) && name.Length > double_.Length)
                return name[..^double_.Length];
        var stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length > 0 ? stem : name;
    }

    /// <summary>A free <c>name.sfe-partial</c> in <paramref name="folder"/> (its name cut to stay a valid one), for a folder or a file.</summary>
    private static string PartialFolder(string folder, string stem)
    {
        stem = stem.Length > 200 ? stem[..200].TrimEnd('.', ' ') : stem;
        for (var n = 1; n <= FileNames.MaxKeepBothAttempts; n++)
        {
            var name = n == 1 ? stem + PartialSuffix : $"{stem} ({n}){PartialSuffix}";
            if (!Taken(folder)(name))
                return Path.Combine(folder, name);
        }
        throw new IOException("No free name.");
    }

    /// <summary><paramref name="name"/> in <paramref name="folder"/> is a path no route takes (the work folder, its 8.3 name).</summary>
    private bool Reserved(string folder, string name) => !paths.TryResolve(Join(folder, name), out _);

    private static Func<string, bool> Taken(string folder) =>
        n => File.Exists(Path.Combine(folder, n)) || Directory.Exists(Path.Combine(folder, n));

    /// <summary>
    /// Partial folders an extraction left in <paramref name="folder"/> when the server stopped during it: removed at the
    /// next extraction there, unless a running job writes into one.
    /// </summary>
    private void RemoveLeftovers(Job job, string folder)
    {
        try
        {
            var running = _jobs.Values.Where(j => j != job && j.State is FileJobState.Running or FileJobState.Waiting).SelectMany(j => j.Partials).ToHashSet(LibraryPaths.PathComparer);
            foreach (var leftover in Directory.EnumerateDirectories(folder, "*" + PartialSuffix))
                if (!running.Contains(leftover) && new DirectoryInfo(leftover).LinkTarget is null)
                    RemovePartial(leftover);
            foreach (var leftover in Directory.EnumerateFiles(folder, "*" + PartialSuffix))
                if (!running.Contains(leftover) && new FileInfo(leftover).LinkTarget is null)
                    RemovePartialFile(leftover);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not look for partial folders in {Folder}", folder);
        }
    }

    private void RemovePartial(string partial)
    {
        try
        {
            if (Directory.Exists(partial))
                FileTree.Delete(partial);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove the partial folder {Folder}: the next extraction there does", partial);
        }
    }

    /// <summary>The thumbnail and trickplay of a copy are those of its original (same content, same modification time).</summary>
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

    private async Task<string?> TooLongForTheIndexAsync(string full, int growth, CancellationToken token)
    {
        if (growth <= 0)
            return null;
        await using var db = await dbFactory.CreateDbContextAsync(token);
        if (db.PathLengthLimit is not { } limit)
            return null;
        var longest = Math.Max(
            await db.Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, full)).MaxAsync(f => (int?)f.FullName.Length, token) ?? 0,
            await db.Directories.Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, full)).MaxAsync(d => (int?)d.FullName.Length, token) ?? 0);
        return longest > 0 && longest + growth > limit
            ? $"“{Path.GetFileName(full)}” can't go there: the full paths of some files would pass the {limit} characters the index can keep."
            : null;
    }

    private static ClashSide Side(string full)
    {
        FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
        return new ClashSide(info.Name, info is DirectoryInfo, info is FileInfo file ? file.Length : null,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    /// <summary>Bytes of a file, or of the files below a folder (links not followed).</summary>
    private static long SizeOf(string full)
    {
        try
        {
            return File.Exists(full) ? new FileInfo(full).Length : FileTree.Files(full).Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long? FreeSpace(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The drive would keep less free space than the server's margin.</summary>
    private sealed class NoRoomException(string message) : IOException(message);

    private static FileJobItem ScanRunning(string path) =>
        new(path, null, FileOperationStatus.ScanRunning, "A scan is running over it: try again when it has finished.");

    private void Prune()
    {
        foreach (var old in _jobs.Values.Where(j => j.State is FileJobState.Completed or FileJobState.Cancelled).OrderByDescending(j => j.Id).Skip(KeptJobs).ToList())
            _jobs.TryRemove(old.Id, out _);
    }

    private static string Parent(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;
    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";
    private static string Base(string path) => path.Split('/')[^1];

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    /// <summary>One move or copy, its progress and its answers.</summary>
    private sealed class Job(int id, FileJobRequest request, FileActor actor, IFileJobAccess access, CancellationTokenSource cancellation)
    {
        private readonly Lock _gate = new();
        private readonly List<FileJobItem> _results = [];
        private ConflictChoice? _forAll = request.Conflicts == ConflictChoice.Ask ? null : request.Conflicts;
        private TaskCompletionSource<ConflictChoice>? _answer;
        private FileJobConflict? _conflict;
        private long _bytesDone;
        private DateTimeOffset? _finished;

        public int Id { get; } = id;
        public FileJobRequest Request { get; } = request;
        public FileActor Actor { get; } = actor;
        public IFileJobAccess Access { get; } = access;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public DateTimeOffset Started { get; } = DateTimeOffset.Now;
        public FileJobState State { get; private set; } = FileJobState.Running;
        public long BytesTotal { get; set; }
        public string? Current { get; set; }
        public Task? Completion { get; set; }

        public void AddBytes(long bytes) => Interlocked.Add(ref _bytesDone, bytes);

        /// <summary>How far the job is in its source (an extraction: in the archive's file).</summary>
        public void SetBytes(long bytes) => Interlocked.Exchange(ref _bytesDone, bytes);

        /// <summary>The archive's password, while the job runs; never shown.</summary>
        public string? Password { get; set; }

        /// <summary>The partial folders the job writes into.</summary>
        public ConcurrentBag<string> Partials { get; } = [];

        public void Report(FileJobItem item)
        {
            lock (_gate)
                _results.Add(item);
        }

        public async Task<ConflictChoice> ChooseAsync(FileJobConflict conflict, CancellationToken token)
        {
            Task<ConflictChoice> answer;
            lock (_gate)
            {
                if (_forAll is { } choice && (choice != ConflictChoice.Replace || conflict.CanReplace))
                    return choice;
                _conflict = conflict;
                _answer = new TaskCompletionSource<ConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
                answer = _answer.Task;
                State = FileJobState.Waiting;
            }
            try
            {
                return await answer.WaitAsync(token);
            }
            finally
            {
                lock (_gate)
                {
                    _conflict = null;
                    _answer = null;
                    if (State == FileJobState.Waiting)
                        State = FileJobState.Running;
                }
            }
        }

        public bool Answer(ConflictChoice choice, bool forAll)
        {
            lock (_gate)
            {
                if (_answer is null)
                    return false;
                if (forAll)
                    _forAll = choice;
                return _answer.TrySetResult(choice);
            }
        }

        public void Finish(FileJobState state)
        {
            lock (_gate)
            {
                State = state;
                _finished = DateTimeOffset.Now;
                Current = null;
            }
        }

        public FileJobView View()
        {
            lock (_gate)
                return new FileJobView(Id, Request.Kind, State, Request.Target,
                    Request.Kind == FileJobKind.Compress ? 1 : Request.Paths.Distinct(StringComparer.Ordinal).Count(), _results.Count,
                    BytesTotal, Interlocked.Read(ref _bytesDone), Current, _conflict, [.. _results], Started, _finished, Actor.AccountId);
        }
    }
}
