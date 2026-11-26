using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Files.Compression;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Search;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// Moves and copies (agents/docs/features/move-and-copy.md): each request is a background job with progress, cancel,
/// questions on name clashes and a final report, re-checking the person's rights at every item. Moves keep what SFE
/// knows by path (<see cref="PathMoves"/>, folder rules); copies never follow links, leave out what the person may not
/// see, are written as <c>name.sfe-partial</c> with their progress told to open listings
/// (<see cref="ItemsBeingWritten"/>), leave nothing half written when cancelled, and are indexed (a scan of a copied
/// folder).
/// </summary>
public sealed class FileJobs(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    DirectoryIndexer indexer,
    LibraryBrowser browser,
    TagService tags,
    LibraryCopies copies,
    ScanCoordinator scans,
    PathMoves moves,
    LibraryClashes clashes,
    JobBoard board,
    TenantBinding tenant,
    IOptionsMonitor<SfeOptions> options,
    ILogger<FileJobs> logger,
    FolderRuleService? rules = null,
    Archives? archives = null,
    ArchiveBuildGate? gates = null
) : IDisposable
{
    private const int CopyBuffer = 1 << 20;

    /// <summary>Bytes written between two reads of the free space while extracting.</summary>
    private const long FreeSpaceStep = 64L << 20;

    private readonly CancellationTokenSource _shutdown = new();
    private readonly PartialItems _partials = new(board, browser, logger);

    /// <summary>Files and folders being written before they take their names (<see cref="LibraryPaths"/>).</summary>
    public const string PartialSuffix = LibraryPaths.PartialSuffix;

    /// <summary>
    /// Starts a move, copy or extraction; the result is its first view. <paramref name="password"/>: the archive's,
    /// held by the job only while it runs (never in its view, its request or the log).
    /// </summary>
    public FileJobView Start(FileJobRequest request, FileActor actor, IFileJobAccess access, string? password = null)
    {
        var job = new JobRun(
            board.NextId(),
            request,
            actor,
            access,
            CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token)
        )
        {
            Password =
                request.Kind is FileJobKind.Extract or FileJobKind.Compress && !string.IsNullOrEmpty(password)
                    ? password
                    : null,
        };
        board.Add(job);
        job.Completion = Task.Run(() => RunAsync(job), CancellationToken.None);
        board.Prune();
        return job.View();
    }

    private async Task RunAsync(JobRun job)
    {
        var request = job.Request;
        var token = job.Cancellation.Token;
        var copiedFolders = new List<string>();
        try
        {
            if (
                LibraryPaths.NormalizeRelative(request.Target) is not { } targetPath
                || !paths.TryResolve(targetPath, out var targetFull)
                || !Directory.Exists(targetFull)
                || job.Access.IsReadOnly(targetFull)
            )
            {
                foreach (var path in request.Paths)
                {
                    job.Report(
                        new FileJobItem(
                            path,
                            null,
                            FileOperationStatus.NotFound,
                            "The folder they would go to was not found."
                        )
                    );
                }

                return;
            }
            targetPath = paths.ToRelative(targetFull)!;
            if (request.Kind is FileJobKind.Copy or FileJobKind.Extract or FileJobKind.Compress)
                job.BytesTotal = request.Paths.Sum(path => paths.TryResolve(path, out var full) ? SizeOf(full) : 0);
            if (request.Kind == FileJobKind.Copy)
                _partials.RemoveLeftovers(job, targetFull);
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
                    made = new FileJobItem(
                        request.Paths.Count == 1 ? request.Paths[0] : targetPath,
                        null,
                        FileOperationStatus.Failed,
                        ex is NoRoomException
                            ? ex.Message
                            : FileOperations.DiskFailure(ex, "The zip could not be written").Message
                    );
                }
                job.Report(made);
                job.Finish(FileJobState.Completed);
                return;
            }

            foreach (var path in request.Paths.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                FileJobItem result;
                try
                {
                    result = await job.BeginAsync(path)
                        ?? (
                            request.Kind == FileJobKind.Extract
                                ? await ExtractAsync(job, path, targetPath, targetFull, copiedFolders, token)
                                : await ItemAsync(job, path, targetPath, targetFull, copiedFolders, token)
                        );
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        ex,
                        "{Account} could not {Kind} {Path} to {Target}",
                        job.Actor.Name,
                        request.Kind.ToString().ToLowerInvariant(),
                        path,
                        targetPath
                    );
                    result = new FileJobItem(path, null, FileOperationStatus.Failed, FailureOf(ex, path, request.Kind));
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

    private async Task<FileJobItem> ItemAsync(
        JobRun job,
        string path,
        string targetPath,
        string targetFull,
        List<string> copiedFolders,
        CancellationToken token
    )
    {
        var request = job.Request;
        var move = request.Kind == FileJobKind.Move;
        var right = move ? Rights.MoveFiles : Rights.CopyFiles;
        if (
            LibraryPaths.NormalizeRelative(path) is not { Length: > 0 }
            || !paths.TryResolve(path, out var source)
            || paths.ToRelative(source) is not { Length: > 0 } sourcePath
            || (!File.Exists(source) && !Directory.Exists(source))
            || job.Access.IsProtected(source)
            || !await job.Access.MayAsync(sourcePath, Rights.Browse)
        )
        {
            return new FileJobItem(path, null, FileOperationStatus.NotFound, "It was not found.");
        }
        // Moving takes it away from where it is: a write there too.
        if (move && job.Access.IsReadOnly(source))
        {
            return new FileJobItem(
                path,
                null,
                FileOperationStatus.Forbidden,
                "It is behind a link leaving the library, which is read-only: copy it instead."
            );
        }
        var isFolder = Directory.Exists(source);
        var name = Path.GetFileName(source);

        // Who may: moves change the source's folder and the target, copies open the source (a folder: its files; the
        // folders below that may not be opened are left out) and change the target.
        var allowed = move
            ? await job.Access.MayAsync(sourcePath, right)
                && await job.Access.MayAsync(Parent(sourcePath), right)
                && (!isFolder || await job.Access.MayThroughoutAsync(sourcePath, right))
            : await job.Access.MayAsync(sourcePath, Rights.ViewFiles);
        if (!allowed || !await job.Access.MayAsync(targetPath, right))
        {
            return new FileJobItem(
                path,
                null,
                FileOperationStatus.Forbidden,
                $"You may not {(move ? "move" : "copy")} “{name}” there."
            );
        }

        if (isFolder && FolderPolicy.IsSameOrBelow(targetPath, sourcePath))
            return new FileJobItem(path, null, FileOperationStatus.Failed, $"“{name}” can't go into itself.");
        if (move && LibraryPaths.PathComparer.Equals(Parent(sourcePath), targetPath))
            return new FileJobItem(path, sourcePath, FileOperationStatus.Skipped, $"“{name}” is already there.");

        // A name clash: the person's choice, asked when needed.
        var (clash, refused, merge) = await clashes.ClashAsync(
            job,
            new ClashCheck
            {
                Path = path,
                Source = source,
                SourcePath = sourcePath,
                IsFolder = isFolder,
                TargetPath = targetPath,
                TargetFull = targetFull,
                Name = name,
                KeepBoth =
                    request.Kind == FileJobKind.Copy
                    && LibraryPaths.PathComparer.Equals(Parent(sourcePath), targetPath),
                MergeRight = right,
            },
            token
        );
        if (refused is not null)
            return refused;
        name = clash!;
        var destination = Path.Combine(targetFull, name);
        var destinationPath = Join(targetPath, name);
        if (merge)
        {
            var merged = new MergedFolder(source, sourcePath, destination, destinationPath);
            return await MergeAsync(job, path, merged, copiedFolders, token);
        }
        if (await TooLongForTheIndexAsync(source, destination.Length - source.Length, token) is { } tooLong)
            return new FileJobItem(path, null, FileOperationStatus.Invalid, tooLong);

        return move
            ? await MoveAsync(job, path, source, sourcePath, destination, destinationPath, isFolder, token)
            : await CopyAsync(
                job,
                path,
                source,
                sourcePath,
                destination,
                destinationPath,
                isFolder,
                copiedFolders,
                token
            );
    }

    /// <summary>
    /// A folder merged into the folder of its name there: each item it holds moved or copied into it as an item of
    /// the job (rights, clash questions, merged again); then, for a move, the folder goes once nothing is left in it.
    /// One result for the folder, saying what was left out.
    /// </summary>
    private async Task<FileJobItem> MergeAsync(
        JobRun job,
        string path,
        MergedFolder folder,
        List<string> copiedFolders,
        CancellationToken token
    )
    {
        var move = job.Request.Kind == FileJobKind.Move;
        if (!move)
            _partials.RemoveLeftovers(job, folder.Into);
        var tally = new MergeTally(Base(folder.SourcePath), folder.IntoPath);
        // Read first: a move changes what the folder holds.
        var entries = new DirectoryInfo(folder.Source)
            .EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true })
            .ToList();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var entryPath = Join(folder.SourcePath, entry.Name);
            // A copy never follows links (as a copied folder's).
            tally.Add(
                !move && entry.LinkTarget is not null
                    ? new FileJobItem(
                        entryPath,
                        null,
                        FileOperationStatus.Skipped,
                        $"“{entry.Name}” was left out: it is a link."
                    )
                    : await MergeEntryAsync(job, entryPath, folder, copiedFolders, token)
            );
        }
        if (move)
            await RemoveEmptiedAsync(job, folder);
        logger.LogInformation(
            "{Account} merged {Path} into {Target}",
            job.Actor.Name,
            folder.SourcePath,
            folder.IntoPath
        );
        return tally.Result(path);
    }

    private async Task<FileJobItem> MergeEntryAsync(
        JobRun job,
        string entryPath,
        MergedFolder folder,
        List<string> copiedFolders,
        CancellationToken token
    )
    {
        try
        {
            return await job.BeginAsync(entryPath)
                ?? await ItemAsync(job, entryPath, folder.IntoPath, folder.Into, copiedFolders, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                ex,
                "{Account} could not merge {Path} into {Target}",
                job.Actor.Name,
                entryPath,
                folder.IntoPath
            );
            return new FileJobItem(
                entryPath,
                null,
                FileOperationStatus.Failed,
                FailureOf(ex, entryPath, job.Request.Kind)
            );
        }
    }

    /// <summary>
    /// The folder a move merged goes when nothing is left in it: its index row, its own folder rules and default tags
    /// with it (what it held took what SFE knew of each). One still holding what was left out stays.
    /// </summary>
    private async Task RemoveEmptiedAsync(JobRun job, MergedFolder folder)
    {
        using var hold = scans.TryBeginOperation(folder.Source);
        try
        {
            if (hold is null || Directory.EnumerateFileSystemEntries(folder.Source).Any())
                return;
            Directory.Delete(folder.Source);
            if (rules is not null && tenant.TenantId is { } tenantId)
                await rules.TakeTreeAsync(tenantId, folder.SourcePath, CancellationToken.None);
            await moves.ForgetMergedAsync(folder.Source, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "{Account} merged {Path}, which stays: it could not be removed",
                job.Actor.Name,
                folder.SourcePath
            );
        }
    }

    /// <summary>The reason an item failed for <paramref name="ex"/>, a failure of the disk.</summary>
    private static string? FailureOf(Exception ex, string path, FileJobKind kind) =>
        ex is LinksInsideException or NoRoomException
            ? ex.Message
            : FileOperations.DiskFailure(ex, $"“{Base(path)}” could not be {Done(kind)}").Message;

    private async Task<FileJobItem> MoveAsync(
        JobRun job,
        string path,
        string source,
        string sourcePath,
        string destination,
        string destinationPath,
        bool isFolder,
        CancellationToken token
    )
    {
        using var hold = scans.TryBeginOperation(source, destination);
        if (hold is null)
            return ScanRunning(path);

        FolderRuleMove? ruleMove = null;
        if (isFolder && rules is not null && tenant.TenantId is { } tenantId)
        {
            var begun = await rules.BeginMoveAsync(
                tenantId,
                sourcePath,
                destinationPath,
                job.Request.KeepAccess,
                job.Actor.AccountId,
                token
            );
            if (begun.Status != AccessStatus.Ok)
            {
                return new FileJobItem(
                    path,
                    null,
                    FileOperationStatus.Conflict,
                    begun.Problem is not null
                        ? $"“{Base(destinationPath)}” can't go there: folder rules are set for a folder of that "
                            + $"name. An administrator can remove them or point them elsewhere."
                        : begun.Errors?.Values.SelectMany(e => e).FirstOrDefault()
                );
            }

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

    private async Task<FileJobItem> CopyAsync(
        JobRun job,
        string path,
        string source,
        string sourcePath,
        string destination,
        string destinationPath,
        bool isFolder,
        List<string> copiedFolders,
        CancellationToken token
    )
    {
        var needed = SizeOf(source);
        var margin = Math.Max(0, options.CurrentValue.Files.MinimumFreeSpaceMegabytes) * 1024L * 1024;
        if (DiskSpace.FreeBytes(Path.GetDirectoryName(destination)!) is { } free && free - needed < margin)
        {
            return new FileJobItem(
                path,
                null,
                FileOperationStatus.Failed,
                $"“{Base(path)}” was not copied: the drive would keep less than {margin / 1024 / 1024:N0} MB free."
            );
        }
        // Written as name.sfe-partial, listed at once with its progress, renamed when whole.
        var partial = PartialItems.FreeName(Path.GetDirectoryName(destination)!, Path.GetFileName(destination));
        using var hold = scans.TryBeginOperation(partial, destination);
        if (hold is null)
            return ScanRunning(path);
        var skipped = new List<string>();
        job.Partials.Add(partial);
        try
        {
            _partials.Writes(job, PartialItems.Create(partial, isFolder), Path.GetFileName(destination), needed);
            if (isFolder)
                await copies.FolderAsync(job, source, sourcePath, partial, skipped, token);
            else
                await LibraryCopies.FileAsync(job, source, partial, token);
            destination = PartialItems.Place(partial, destination, isFolder);
            destinationPath = Join(Parent(destinationPath), Path.GetFileName(destination));
        }
        catch
        {
            // Cancelled or failed: nothing half copied stays.
            _partials.Remove(partial);
            throw;
        }
        finally
        {
            PartialItems.Wrote(job);
        }

        // The copies: indexed (a file now, a folder by a scan), with previews and, when asked, the originals' tags.
        try
        {
            await copies.AfterAsync(job, source, destination, isFolder, copiedFolders, destinationPath, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The copy of {Path} could not be indexed: the next scan does", sourcePath);
        }
        logger.LogInformation("{Account} copied {Path} to {Target}", job.Actor.Name, sourcePath, destinationPath);
        return new FileJobItem(
            path,
            destinationPath,
            FileOperationStatus.Done,
            skipped.Count == 0
                ? null
                : $"Left out: {string.Join(", ", skipped.Take(5))}{More(skipped.Count, 5)} "
                    + "(links, or folders you may not see)."
        );
    }

    /// <summary>What a job of this kind does to an item, as the end of "could not be …".</summary>
    private static string Done(FileJobKind kind) =>
        kind switch
        {
            FileJobKind.Move => "moved",
            FileJobKind.Copy => "copied",
            _ => "extracted",
        };

    /// <summary>
    /// " and N more" when a list of <paramref name="count"/> names shows only <paramref name="shown"/>.
    /// </summary>
    private static string More(int count, int shown) => count > shown ? $" and {count - shown} more" : string.Empty;

    /// <summary>
    /// Extracts the archive <paramref name="path"/> (its entries chosen, or all) into a partial folder of the target,
    /// then gives what came out its place: the new folder (named after the archive, or after its one top folder), or
    /// each top item in the target with the clash questions. Cancelled or failed (a wrong password, the disk full):
    /// the partial folder goes, nothing half written stays.
    /// </summary>
    private async Task<FileJobItem> ExtractAsync(
        JobRun job,
        string path,
        string targetPath,
        string targetFull,
        List<string> extractedFolders,
        CancellationToken token
    )
    {
        if (
            LibraryPaths.NormalizeRelative(path) is not { Length: > 0 }
            || !paths.TryResolve(path, out var source)
            || paths.ToRelative(source) is not { Length: > 0 } sourcePath
            || !File.Exists(source)
            || job.Access.IsProtected(source)
            || !await job.Access.MayAsync(sourcePath, Rights.Browse)
        )
        {
            return new FileJobItem(path, null, FileOperationStatus.NotFound, "It was not found.");
        }

        var name = Path.GetFileName(source);
        // An archive's content is the file's content: it is opened (view, download) and written where one may extract.
        if (
            !await job.Access.MayAsync(sourcePath, Rights.ViewFiles)
            || !await job.Access.MayAsync(sourcePath, Rights.Download)
            || !await job.Access.MayAsync(targetPath, Rights.ExtractFiles)
        )
        {
            return new FileJobItem(path, null, FileOperationStatus.Forbidden, $"You may not extract “{name}” there.");
        }

        if (ArchiveFormats.Detect(source) is not { } format)
        {
            return new FileJobItem(
                path,
                null,
                FileOperationStatus.Invalid,
                $"“{name}” is not an archive SFE extracts (zip, 7z, rar, tar, gz, bz2, xz)."
            );
        }

        var settings = options.CurrentValue;
        var stem = StemOf(name);
        _partials.RemoveLeftovers(job, targetFull);
        var partial = PartialItems.FreeName(targetFull, stem);
        using var hold = scans.TryBeginOperation(partial);
        if (hold is null)
            return ScanRunning(path);
        job.Partials.Add(partial);

        ExtractResult extracted;
        try
        {
            Directory.CreateDirectory(partial);
            _partials.Writes(job, partial, stem, SizeOf(source));
            int? maxFullPath = null;
            await using (var db = await dbFactory.CreateDbContextAsync(token))
                maxFullPath = db.PathLengthLimit;
            var archives = settings.Files.Archives;
            var limits = new ExtractLimits(
                archives.MaxDepth,
                archives.MaxEntries,
                archives.MaxCompressionRatio,
                settings.Files.PortableNames,
                maxFullPath,
                archives.MaxSevenZipListMegabytes * 1024L * 1024
            );
            var margin = Math.Max(0, settings.Files.MinimumFreeSpaceMegabytes) * 1024L * 1024;
            var unchecked_ = FreeSpaceStep; // the first file: read at once
            extracted = ArchiveExtractor.Extract(
                source,
                format,
                job.Password,
                partial,
                job.Request.Entries,
                limits,
                reserve: size =>
                {
                    // The free space, read before each big file and every 64 MB of small ones.
                    unchecked_ += size;
                    if (unchecked_ < FreeSpaceStep && size < FreeSpaceStep)
                        return;
                    unchecked_ = 0;
                    if (DiskSpace.FreeBytes(partial) is { } free && free - size < margin)
                    {
                        throw new NoRoomException(
                            $"“{name}” was not extracted: the drive would keep less than {margin / 1024 / 1024:N0} MB "
                                + $"free."
                        );
                    }
                },
                read: job.SetBytes,
                cancel: token
            );
        }
        catch (Exception ex)
        {
            _partials.RemoveFolder(partial);
            if (ex is ArchiveException { NeedsPassword: true })
            {
                return new FileJobItem(
                    path,
                    null,
                    FileOperationStatus.NeedsPassword,
                    string.IsNullOrEmpty(job.Password)
                        ? $"“{name}” is protected by a password: extract it with its password."
                        : $"That password doesn't open “{name}”: nothing was extracted."
                );
            }

            if (ex is ArchiveException)
            {
                return new FileJobItem(
                    path,
                    null,
                    FileOperationStatus.Failed,
                    $"“{name}” could not be extracted: {JobMessages.Lower(ex.Message)}"
                );
            }

            throw;
        }
        finally
        {
            PartialItems.Wrote(job);
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
                var taken = (string n) => LibraryClashes.Taken(targetFull)(n) || clashes.Reserved(targetPath, n);
                if (taken(folderName))
                    folderName = FileNames.KeepBoth(folderName, true, taken) ?? throw new IOException("No free name.");
                var destination = Path.Combine(targetFull, folderName);
                using var place = scans.TryBeginOperation(destination);
                if (place is null)
                {
                    _partials.RemoveFolder(partial);
                    return ScanRunning(path);
                }
                Directory.Move(single ?? partial, destination);
                placed.Add((destination, true));
                newPath = Join(targetPath, folderName);
            }
            else
            {
                skipped = await PlaceAsync(
                    job,
                    path,
                    new MergedFolder(partial, "", targetFull, targetPath),
                    placed,
                    token
                );
            }
        }
        finally
        {
            _partials.RemoveFolder(partial);
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
            logger.LogWarning(
                ex,
                "What was extracted from {Path} could not be indexed: the next scan does",
                sourcePath
            );
        }
        logger.LogInformation(
            "{Account} extracted {Path} to {Target}: {Files} files, {Folders} folders, {LeftOut} left out",
            job.Actor.Name,
            sourcePath,
            newPath ?? targetPath,
            extracted.Files,
            extracted.Folders,
            extracted.LeftOutCount
        );
        return new FileJobItem(path, newPath, FileOperationStatus.Done, JobMessages.Extracted(extracted, skipped));
    }

    /// <summary>
    /// What was extracted into <c>from.Source</c> (the partial folder, or a folder of it) given its place in
    /// <c>from.Into</c>: each name taken asked (a folder may be merged, its items placed in turn). The result is how
    /// many were left out.
    /// </summary>
    private async Task<int> PlaceAsync(
        JobRun job,
        string path,
        MergedFolder from,
        List<(string Full, bool IsFolder)> placed,
        CancellationToken token
    )
    {
        var skipped = 0;
        foreach (var item in Directory.EnumerateFileSystemEntries(from.Source).ToList())
        {
            var isFolder = Directory.Exists(item);
            var (itemName, refused, merge) = await clashes.ClashAsync(
                job,
                new ClashCheck
                {
                    Path = path,
                    Source = item,
                    IsFolder = isFolder,
                    TargetPath = from.IntoPath,
                    TargetFull = from.Into,
                    Name = Path.GetFileName(item),
                    MergeRight = Rights.ExtractFiles,
                },
                token
            );
            var destination = refused is null ? Path.Combine(from.Into, itemName!) : null;
            if (destination is not null && merge)
            {
                var into = new MergedFolder(item, "", destination, Join(from.IntoPath, itemName!));
                skipped += await PlaceAsync(job, path, into, placed, token);
                continue;
            }
            using var place = destination is null ? null : scans.TryBeginOperation(destination);
            if (place is null)
            {
                skipped++;
                continue;
            }
            FileTree.Move(item, destination!, isFolder);
            placed.Add((destination!, isFolder));
        }
        return skipped;
    }

    /// <summary>
    /// Compresses the request's items into one zip in the target (agents/docs/features/compression.md): the walk of the
    /// downloads (<see cref="Archives"/>: links never followed, what the person may not see left out) into SFE's writer
    /// (the level asked, a password), one compression at a time on the server. Written into a partial file, then given
    /// its name (a clash asked then); cancelled or failed, the partial file goes.
    /// </summary>
    private async Task<FileJobItem> CompressAsync(
        JobRun job,
        string targetPath,
        string targetFull,
        CancellationToken token
    )
    {
        var request = job.Request;
        var label = request.Paths.Count == 1 ? request.Paths[0] : targetPath;
        if (archives is null || gates is null)
        {
            return new FileJobItem(
                label,
                null,
                FileOperationStatus.Failed,
                "Compressing is not available on this server."
            );
        }

        if (
            !await job.Access.MayAsync(targetPath, Rights.CompressFiles)
            || await job.Access.ArchiveAccessAsync() is not { } access
        )
        {
            return new FileJobItem(
                label,
                null,
                FileOperationStatus.Forbidden,
                "You may not compress into this folder."
            );
        }

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
        if (DiskSpace.FreeBytes(targetFull) is { } free && free - bound < margin)
        {
            return new FileJobItem(
                label,
                null,
                FileOperationStatus.Failed,
                $"“{name}” was not made: the drive would keep less than {margin / 1024 / 1024:N0} MB free."
            );
        }

        // One compression at a time on the server: the others wait their turn (the panel shows them running).
        var gate = gates.Compressions(settings.Files.Archives.CompressJobs);
        await gate.WaitAsync(token);
        try
        {
            _partials.RemoveLeftovers(job, targetFull);
            var partial = PartialItems.FreeName(targetFull, name);
            using var hold = scans.TryBeginOperation(partial);
            if (hold is null)
                return ScanRunning(label);
            job.Partials.Add(partial);
            ArchiveSummary summary;
            long zipLength;
            try
            {
                await using var file = new FileStream(
                    partial,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    CopyBuffer
                );
                await using var writer = new ZipWriter(file, job.Password);
                _partials.Writes(job, partial, name, job.BytesTotal);
                var checkedAt = 0L;
                summary = await archives.CompressAsync(
                    chosen,
                    writer,
                    new CompressOptions(level, request.SkipCompressed, LibraryPaths.RealPath(partial)),
                    access,
                    read: count => job.AddBytes(count),
                    written: length =>
                    {
                        // The free space, read every 64 MB written.
                        if (length - checkedAt < FreeSpaceStep)
                            return;
                        checkedAt = length;
                        if (DiskSpace.FreeBytes(targetFull) is { } left && left < margin)
                        {
                            throw new NoRoomException(
                                $"“{name}” was not made: the drive would keep less than {margin / 1024 / 1024:N0} MB "
                                    + $"free."
                            );
                        }
                    },
                    cancellationToken: token
                );
                writer.Finish();
                zipLength = file.Length;
            }
            catch
            {
                _partials.RemoveFile(partial);
                throw;
            }
            finally
            {
                PartialItems.Wrote(job);
            }

            // Its name, now that it is whole: a clash is asked (or answered by the request).
            var (finalName, clash, _) = await clashes.ClashAsync(
                job,
                new ClashCheck
                {
                    Path = label,
                    Source = partial,
                    IsFolder = false,
                    TargetPath = targetPath,
                    TargetFull = targetFull,
                    Name = name,
                },
                token
            );
            if (clash is not null)
            {
                _partials.RemoveFile(partial);
                return clash;
            }
            var destination = Path.Combine(targetFull, finalName!);
            using (var place = scans.TryBeginOperation(destination))
            {
                if (place is null)
                {
                    _partials.RemoveFile(partial);
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
            logger.LogInformation(
                "{Account} compressed {Count} items into {Path}: {Files} files, {Bytes} bytes into {Zip} (level "
                    + "{Level}{Encrypted})",
                job.Actor.Name,
                chosen.Items.Count,
                newPath,
                summary.Files,
                summary.Bytes,
                zipLength,
                level,
                job.Password is null ? "" : ", encrypted"
            );
            return new FileJobItem(
                label,
                newPath,
                FileOperationStatus.Done,
                JobMessages.Compressed(summary, zipLength, job.Password is not null)
            );
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>What a compression says when it is done: what went in, the ratio, what was left out.</summary>
    /// <summary>The archive's name without what makes it an archive: <c>trip.tar.gz</c> → <c>trip</c>.</summary>
    internal static string StemOf(string name)
    {
        foreach (var double_ in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
        {
            if (name.EndsWith(double_, StringComparison.OrdinalIgnoreCase) && name.Length > double_.Length)
                return name[..^double_.Length];
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length > 0 ? stem : name;
    }

    private async Task<string?> TooLongForTheIndexAsync(string full, int growth, CancellationToken token)
    {
        if (growth <= 0)
            return null;
        await using var db = await dbFactory.CreateDbContextAsync(token);
        if (db.PathLengthLimit is not { } limit)
            return null;
        var longest = Math.Max(
            await db
                .Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, full))
                .MaxAsync(f => (int?)f.FullName.Length, token)
                ?? 0,
            await db
                .Directories.Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, full))
                .MaxAsync(d => (int?)d.FullName.Length, token)
                ?? 0
        );
        return longest > 0 && longest + growth > limit
            ? $"“{Path.GetFileName(full)}” can't go there: the full paths of some files would pass the {limit} "
                + $"characters the index can keep."
            : null;
    }

    /// <summary>Bytes of a file, or of the files below a folder (links not followed).</summary>
    private static long SizeOf(string full)
    {
        try
        {
            return File.Exists(full)
                ? new FileInfo(full).Length
                : FileTree.Files(full).Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>The drive would keep less free space than the server's margin.</summary>
    private sealed class NoRoomException(string message) : IOException(message);

    private static FileJobItem ScanRunning(string path) => LibraryClashes.ScanRunning(path);

    private static string Parent(string path) =>
        path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;

    private static string Join(string folder, string name) => LibraryClashes.Join(folder, name);

    private static string Base(string path) => path.Split('/')[^1];

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
