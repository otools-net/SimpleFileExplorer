using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Data;
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

/// <param name="Paths">Files and folders to move or copy (root-relative).</param>
/// <param name="Target">The folder they go into.</param>
/// <param name="KeepAccess">Moved folders keep the access each group and account had (folder rules pinning it).</param>
/// <param name="KeepTags">Copies get the tags of their originals.</param>
public sealed record FileJobRequest(FileJobKind Kind, IReadOnlyList<string> Paths, string Target, ConflictChoice Conflicts = ConflictChoice.Ask,
    bool KeepAccess = false, bool KeepTags = false);

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
    FolderRuleService? rules = null) : IDisposable
{
    /// <summary>Finished jobs kept for the operations panel.</summary>
    private const int KeptJobs = 50;
    private const int CopyBuffer = 1 << 20;

    private readonly ConcurrentDictionary<int, Job> _jobs = new();
    private readonly CancellationTokenSource _shutdown = new();
    private int _nextId;

    /// <summary>Starts a move or copy; the result is its first view.</summary>
    public FileJobView Start(FileJobRequest request, FileActor actor, IFileJobAccess access)
    {
        var job = new Job(Interlocked.Increment(ref _nextId), request, actor, access, CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token));
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
            if (request.Kind == FileJobKind.Copy)
                job.BytesTotal = request.Paths.Sum(path => paths.TryResolve(path, out var full) ? SizeOf(full) : 0);

            foreach (var path in request.Paths.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                job.Current = path;
                FileJobItem result;
                try
                {
                    result = await ItemAsync(job, path, targetPath, targetFull, copiedFolders, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "{Account} could not {Kind} {Path} to {Target}", job.Actor.Name, request.Kind.ToString().ToLowerInvariant(), path, targetPath);
                    result = new FileJobItem(path, null, FileOperationStatus.Failed,
                        ex is LinksInsideException ? ex.Message : FileOperations.DiskFailure(ex, $"“{Base(path)}” could not be {(request.Kind == FileJobKind.Move ? "moved" : "copied")}").Message);
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
        var destination = Path.Combine(targetFull, name);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            var replaceRight = trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles;
            var existingPath = Join(targetPath, name);
            // Never what holds the item itself (moving X/X next to X, then replacing X).
            var canReplace = !FolderPolicy.IsSameOrBelow(sourcePath, existingPath)
                && await job.Access.MayAsync(existingPath, replaceRight) && await job.Access.MayAsync(targetPath, replaceRight)
                && (!Directory.Exists(destination) || await job.Access.MayThroughoutAsync(existingPath, replaceRight));
            var choice = request.Kind == FileJobKind.Copy && LibraryPaths.PathComparer.Equals(Parent(sourcePath), targetPath)
                ? ConflictChoice.KeepBoth   // a copy in its own folder
                : await job.ChooseAsync(new FileJobConflict(Side(source), Side(destination), canReplace), token);
            switch (choice)
            {
                case ConflictChoice.Skip:
                    return new FileJobItem(path, null, FileOperationStatus.Skipped, $"“{name}” was left out: there is one of that name there.");
                case ConflictChoice.KeepBoth:
                    name = FileNames.KeepBoth(name, isFolder, n => File.Exists(Path.Combine(targetFull, n)) || Directory.Exists(Path.Combine(targetFull, n)))
                        ?? throw new IOException("No free name.");
                    destination = Path.Combine(targetFull, name);
                    break;
                case ConflictChoice.Replace when canReplace:
                    using (var hold = scans.TryBeginOperation(destination))
                    {
                        if (hold is null)
                            return ScanRunning(path);
                        if (trash.AlwaysPermanent)
                            await trash.DeleteForGoodAsync(destination, existingPath, Directory.Exists(destination), job.Actor, token);
                        else
                            await trash.MoveInAsync(destination, existingPath, Directory.Exists(destination), job.Actor, token);
                    }
                    break;
                default:
                    return new FileJobItem(path, null, FileOperationStatus.Forbidden, $"You may not replace “{name}” there.");
            }
        }
        var destinationPath = Join(targetPath, name);
        if (await TooLongForTheIndexAsync(source, destination.Length - source.Length, token) is { } tooLong)
            return new FileJobItem(path, null, FileOperationStatus.Invalid, tooLong);

        return move
            ? await MoveAsync(job, path, source, sourcePath, destination, destinationPath, isFolder, token)
            : await CopyAsync(job, path, source, sourcePath, destination, destinationPath, isFolder, copiedFolders, token);
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
                foreach (var (file, tagIds) in tagged)
                {
                    if (copies.TryGetValue(file, out var fileId))
                        foreach (var tagId in tagIds)
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
                return new FileJobView(Id, Request.Kind, State, Request.Target, Request.Paths.Distinct(StringComparer.Ordinal).Count(), _results.Count,
                    BytesTotal, Interlocked.Read(ref _bytesDone), Current, _conflict, [.. _results], Started, _finished, Actor.AccountId);
        }
    }
}
