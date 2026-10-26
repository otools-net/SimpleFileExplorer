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

namespace Sfe.Core.Files;

public enum FileOperationStatus
{
    Done,

    /// <summary>The file or folder (or the folder to create one in) is not in the library.</summary>
    NotFound,

    /// <summary>The name can't be used (<see cref="FileNames.Check"/>, reserved, too long for the index).</summary>
    Invalid,

    /// <summary>Something of that name is already there (seen or not by the asker: they are told nothing else).</summary>
    Exists,

    /// <summary>A scan runs over that place: try again once it has finished.</summary>
    ScanRunning,

    /// <summary>Something else keeps it from being done now (folder rules left where the folder would go).</summary>
    Conflict,

    /// <summary>The disk refused: in use, or the server's account may not write there.</summary>
    Failed,

    /// <summary>Left as it was (a move to where it is, a name clash answered "skip").</summary>
    Skipped,

    /// <summary>The caller may not do it there (refused by the routes).</summary>
    Forbidden,

    /// <summary>It can't go to the trash (a folder holding links, on another drive than the trash): delete it permanently instead?</summary>
    TrashUnavailable,

    /// <summary>An archive protected by a password that wasn't given, or a wrong one: nothing was extracted.</summary>
    NeedsPassword,
}

/// <param name="Item">The new or renamed file or folder.</param>
/// <param name="Message">Why it was not done, for the person (never a server path).</param>
/// <param name="TrashId">The trash entry of what was deleted to the trash (for an Undo).</param>
public sealed record FileOperationResult(FileOperationStatus Status, LibraryItem? Item = null, string? Message = null, int? TrashId = null)
{
    public static FileOperationResult Fail(FileOperationStatus status, string message) => new(status, Message: message);
}

/// <summary>Who changes the library's files, for the log and the folder rules' history.</summary>
public sealed record FileActor(int? AccountId, string Name);

/// <summary>
/// The changes the web client makes to the library's files (agents/docs/development/file-management-plan.md): new
/// folders and renames for now. Paths are root-relative; the routes have checked the rights and folder rules of the
/// person (<see cref="FolderPolicy.AllowsThroughout"/> for a folder renamed). New names never replace anything: an
/// existing name, even one the person can't see, answers <see cref="FileOperationStatus.Exists"/>.
/// </summary>
public sealed class FileOperations(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    LibraryBrowser browser,
    DirectoryIndexer indexer,
    ScanCoordinator scans,
    PathMoves moves,
    Trash trash,
    TenantBinding tenant,
    IOptionsMonitor<SfeOptions> options,
    ILogger<FileOperations> logger,
    FolderRuleService? rules = null)
{
    /// <summary>Tries of a rename refused because a file is in use (a thumbnail being made...), and the wait between them.</summary>
    private const int InUseAttempts = 3;
    private static readonly TimeSpan InUseDelay = TimeSpan.FromMilliseconds(250);

    private bool PortableNames => options.CurrentValue.Files.PortableNames;

    /// <summary>Creates the folder <paramref name="name"/> in <paramref name="parent"/>.</summary>
    public async Task<FileOperationResult> CreateFolderAsync(string? parent, string? name, FileActor actor, CancellationToken cancellationToken = default)
    {
        if (LibraryPaths.NormalizeRelative(parent) is not { } parentPath || !paths.TryResolve(parentPath, out var parentFull) || !Directory.Exists(parentFull))
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "The folder was not found.");
        if (Target(parentPath, name) is { Error: { } error })
            return error;
        var target = Path.Join(parentFull, name);
        if (File.Exists(target) || Directory.Exists(target))
            return Exists(name!);

        using var operation = scans.TryBeginOperation(target);
        if (operation is null)
            return ScanRunning();
        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Account} could not create the folder {Name} in {Folder}", actor.Name, name, Display(parentPath));
            return DiskFailure(ex, $"The folder “{name}” could not be created");
        }

        // Indexed at once where its parent is, so that it isn't shown as "not indexed" until the next scan.
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await indexer.FolderIdAsync(db, parentFull, create: false, cancellationToken) is not null)
                await indexer.FolderIdAsync(db, target, create: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not index the new folder {Name}", target);
        }

        browser.Invalidate(parentFull);
        var item = browser.ToItem(new DirectoryInfo(target))!;
        logger.LogInformation("{Account} created the folder {Path}", actor.Name, item.Path);
        return new FileOperationResult(FileOperationStatus.Done, item);
    }

    /// <summary>
    /// Creates the file <paramref name="name"/> in the folder <paramref name="parent"/>, empty or with
    /// <paramref name="content"/> (an editor's "Save a copy"), indexed at once.
    /// </summary>
    public async Task<FileOperationResult> CreateFileAsync(string? parent, string? name, FileActor actor, CancellationToken cancellationToken = default,
        byte[]? content = null)
    {
        if (LibraryPaths.NormalizeRelative(parent) is not { } parentPath || !paths.TryResolve(parentPath, out var parentFull) || !Directory.Exists(parentFull))
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "The folder was not found.");
        if (Target(parentPath, name) is { Error: { } error })
            return error;
        var target = Path.Join(parentFull, name);
        if (File.Exists(target) || Directory.Exists(target))
            return Exists(name!);

        using var operation = scans.TryBeginOperation(target);
        if (operation is null)
            return ScanRunning();
        FileStream stream;
        try
        {
            // Never over something that appeared since the check.
            stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        }
        catch (IOException) when (File.Exists(target) || Directory.Exists(target))
        {
            return Exists(name!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Account} could not create the file {Name} in {Folder}", actor.Name, name, Display(parentPath));
            return DiskFailure(ex, $"The file “{name}” could not be created");
        }
        try
        {
            await using (stream)
            {
                if (content is { Length: > 0 })
                    await stream.WriteAsync(content, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Half a file is no copy: what was made goes.
            try
            {
                File.Delete(target);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(cleanup, "Could not delete the half-written file {Name}", target);
            }
            if (ex is OperationCanceledException)
                throw;
            logger.LogWarning(ex, "{Account} could not write the file {Name} in {Folder}", actor.Name, name, Display(parentPath));
            return DiskFailure(ex, $"The file “{name}” could not be written");
        }

        try
        {
            await indexer.IndexFilesAsync([target], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not index the new file {Name}", target);
        }

        browser.Invalidate(parentFull);
        var item = browser.ToItem(new FileInfo(target))!;
        logger.LogInformation("{Account} created the file {Path}", actor.Name, item.Path);
        return new FileOperationResult(FileOperationStatus.Done, item);
    }

    /// <summary>
    /// Renames the file or folder <paramref name="path"/> to <paramref name="name"/>, in the same folder: what SFE
    /// keeps by path (index rows and tags, folder rules, settings, quick links, caches) follows it.
    /// </summary>
    public async Task<FileOperationResult> RenameAsync(string? path, string? name, FileActor actor, CancellationToken cancellationToken = default)
    {
        if (LibraryPaths.NormalizeRelative(path) is not { Length: > 0 } relative || !paths.TryResolve(relative, out var full)
            || paths.ToRelative(full) is not { Length: > 0 } canonical)
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "The file or folder was not found.");
        var isFolder = Directory.Exists(full);
        if (!isFolder && !File.Exists(full))
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "The file or folder was not found.");

        var parentPath = ParentOf(canonical);
        if (Target(parentPath, name) is { Error: { } error })
            return error;
        var target = Path.Join(Path.GetDirectoryName(full)!, name);
        var targetPath = parentPath.Length == 0 ? name! : $"{parentPath}/{name}";
        if (string.Equals(Path.GetFileName(full), name, StringComparison.Ordinal))
            return new FileOperationResult(FileOperationStatus.Done, Item(full, isFolder));

        // On Windows "photo.jpg" → "Photo.jpg" is the same file: allowed (through a temporary name).
        var caseOnly = LibraryPaths.PathComparer.Equals(full, target);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
            return Exists(name!);
        if (await TooLongForTheIndexAsync(full, target.Length - full.Length, cancellationToken) is { } tooLong)
            return FileOperationResult.Fail(FileOperationStatus.Invalid, tooLong);

        using var operation = scans.TryBeginOperation(full, target);
        if (operation is null)
            return ScanRunning();

        FolderRuleMove? ruleMove = null;
        if (isFolder && rules is not null && tenant.TenantId is { } tenantId)
        {
            var begun = await rules.BeginMoveAsync(tenantId, canonical, targetPath, keepAccess: false, actor.AccountId, cancellationToken);
            if (begun.Status != AccessStatus.Ok)
                return FileOperationResult.Fail(FileOperationStatus.Conflict, begun.Problem is not null
                    ? $"“{name}” can't be used here: folder access rules are set for a folder of that name. An administrator can remove them or point them elsewhere (Folder access)."
                    : begun.Errors?.Values.SelectMany(e => e).FirstOrDefault() ?? "The folder's access rules can't follow it there.");
            ruleMove = begun.Value;
        }

        await using (ruleMove)
        {
            try
            {
                await MoveOnDiskAsync(full, target, isFolder, caseOnly, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (ruleMove is not null)
                    await ruleMove.AbortAsync(CancellationToken.None);
                logger.LogWarning(ex, "{Account} could not rename {Path} to {Name}", actor.Name, canonical, name);
                return File.Exists(target) || (!caseOnly && Directory.Exists(target))
                    ? Exists(name!)
                    : DiskFailure(ex, $"“{Path.GetFileName(full)}” could not be renamed");
            }

            // The disk has changed: what follows can't undo it, so it no longer stops when the person leaves.
            await moves.MoveAsync(full, target, isFolder, CancellationToken.None);
            if (ruleMove is not null)
                await ruleMove.CompleteAsync(CancellationToken.None);
        }

        logger.LogInformation("{Account} renamed {Path} to {Name}", actor.Name, canonical, name);
        return new FileOperationResult(FileOperationStatus.Done, Item(target, isFolder));
    }

    /// <summary>
    /// Deletes the file or folder <paramref name="path"/>: to the trash, or for good with <paramref name="permanent"/>
    /// (the routes decide it from the request and <see cref="FileManagementOptions.DeletePermanently"/>). A link is
    /// deleted as a link.
    /// </summary>
    public async Task<FileOperationResult> DeleteAsync(string? path, bool permanent, FileActor actor, CancellationToken cancellationToken = default)
    {
        if (LibraryPaths.NormalizeRelative(path) is not { Length: > 0 } relative || !paths.TryResolve(relative, out var full)
            || paths.ToRelative(full) is not { Length: > 0 } canonical)
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "The file or folder was not found.");
        var isFolder = Directory.Exists(full);
        if (!isFolder && !File.Exists(full))
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "The file or folder was not found.");

        using var operation = scans.TryBeginOperation(full);
        if (operation is null)
            return ScanRunning();
        var name = Path.GetFileName(full);
        try
        {
            if (permanent)
            {
                await trash.DeleteForGoodAsync(full, canonical, isFolder, actor, cancellationToken);
                return new FileOperationResult(FileOperationStatus.Done);
            }
            return new FileOperationResult(FileOperationStatus.Done, TrashId: await trash.MoveInAsync(full, canonical, isFolder, actor, cancellationToken));
        }
        catch (LinksInsideException)
        {
            return FileOperationResult.Fail(FileOperationStatus.TrashUnavailable,
                $"“{name}” can't go to the trash: it is on another drive and holds links. Delete it permanently instead?");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Account} could not delete {Path}", actor.Name, canonical);
            return DiskFailure(ex, $"“{name}” could not be deleted");
        }
    }

    /// <summary>Why <paramref name="name"/> can't be a new name in the folder <paramref name="parent"/>; null when it can.</summary>
    private (FileOperationResult? Error, string? Path) Target(string parent, string? name)
    {
        if (FileNames.Check(name, PortableNames) is { } message)
            return (FileOperationResult.Fail(FileOperationStatus.Invalid, message), null);
        var path = parent.Length == 0 ? name! : $"{parent}/{name}";
        // SFE's work folder at the root, or a name the system would read as another one.
        if (!paths.TryResolve(path, out var full) || !LibraryPaths.PathComparer.Equals(Path.GetFileName(full), name))
            return (FileOperationResult.Fail(FileOperationStatus.Invalid, $"“{name}” is reserved for SFE's own files: pick another name."), null);
        return (null, path);
    }

    private static async Task MoveOnDiskAsync(string from, string to, bool isFolder, bool caseOnly, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (caseOnly)
                {
                    // Some systems take a move to the same path (other case) as nothing to do: go through another name.
                    var temporary = Path.Join(Path.GetDirectoryName(from)!, $".sfe-rename-{Guid.NewGuid():N}");
                    Move(from, temporary);
                    try
                    {
                        Move(temporary, to);
                    }
                    catch
                    {
                        Move(temporary, from);
                        throw;
                    }
                }
                else
                {
                    Move(from, to);
                }
                return;
            }
            catch (IOException ex) when (attempt < InUseAttempts && IsInUse(ex))
            {
                // A thumbnail or a fingerprint being made holds the file for a moment.
                await Task.Delay(InUseDelay, cancellationToken);
            }
        }

        void Move(string source, string destination)
        {
            if (isFolder)
                Directory.Move(source, destination);
            else
                File.Move(source, destination, overwrite: false);
        }
    }

    /// <summary>
    /// Why the index could not hold the new paths of what is at <paramref name="full"/> once longer by
    /// <paramref name="growth"/> characters (SQL Server keeps paths to <see cref="SfeDbContext.MaxPathLength"/>); null when it can.
    /// </summary>
    private async Task<string?> TooLongForTheIndexAsync(string full, int growth, CancellationToken cancellationToken)
    {
        if (growth <= 0)
            return null;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (db.PathLengthLimit is not { } limit)
            return null;
        var longest = Math.Max(
            await db.Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, full)).MaxAsync(f => (int?)f.FullName.Length, cancellationToken) ?? 0,
            await db.Directories.Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, full)).MaxAsync(d => (int?)d.FullName.Length, cancellationToken) ?? 0);
        return longest > 0 && longest + growth > limit
            ? $"The new name is too long: the full paths of some files would pass the {limit} characters the index can keep. Pick a shorter name."
            : null;
    }

    private LibraryItem Item(string full, bool isFolder) =>
        browser.ToItem(isFolder ? new DirectoryInfo(full) : new FileInfo(full))!;

    private static FileOperationResult Exists(string name) =>
        FileOperationResult.Fail(FileOperationStatus.Exists, $"There is already a file or folder named “{name}” here.");

    private static FileOperationResult ScanRunning() =>
        FileOperationResult.Fail(FileOperationStatus.ScanRunning, "A scan is running over this folder: try again when it has finished.");

    /// <summary>The disk refused: said without the server's paths (the exception's message has them).</summary>
    internal static FileOperationResult DiskFailure(Exception ex, string what) => FileOperationResult.Fail(FileOperationStatus.Failed, ex switch
    {
        UnauthorizedAccessException => $"{what}: the server's account may not change files there (it needs write access to the library's folder).",
        IOException io when IsInUse(io) => $"{what}: it is in use, by another program or a video playing. Close it and try again.",
        // Windows answers "access denied" when a folder holds an open file, as when the account may not write.
        IOException { HResult: AccessDenied } => $"{what}: a file in it is in use (another program, a video playing), or the server's account may not change files there.",
        _ => $"{what}: the disk refused it.",
    });

    private const int AccessDenied = unchecked((int)0x80070005);

    /// <summary>A sharing or lock violation (Windows): another program has the file open.</summary>
    private static bool IsInUse(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    private string Display(string relative) => relative.Length == 0 ? paths.RootName : relative;

    private static string ParentOf(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;
}
