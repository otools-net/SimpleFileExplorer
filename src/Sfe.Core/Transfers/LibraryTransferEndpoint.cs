using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Paths;
using static Sfe.Core.Transfers.LibraryTransferErrors;

namespace Sfe.Core.Transfers;

/// <summary>
/// The library's side of a transfer (agents/docs/development/connections-plan.md, phase 5), with the checks of the
/// library's own moves and copies (agents/docs/features/move-and-copy.md): paths that leave the library or reach the
/// work folder are not found, links are not followed in folders, the person's rights and folder rules are read again
/// at every call. Files are written into a partial file renamed at commit, under a scan turn; what is replaced or
/// moved away goes to SFE's trash.
/// </summary>
public sealed class LibraryTransferEndpoint : ITransferEndpoint
{
    private readonly LibraryTransfers _library;
    private readonly FileActor _actor;
    private readonly IFileJobAccess _access;
    private readonly string _writeRight;
    private readonly ConcurrentBag<string> _partials;

    internal LibraryTransferEndpoint(
        LibraryTransfers library,
        FileActor actor,
        IFileJobAccess access,
        string writeRight,
        ConcurrentBag<string> partials
    )
    {
        _library = library;
        _actor = actor;
        _access = access;
        _writeRight = writeRight;
        _partials = partials;
    }

    private LibraryPaths Paths => _library.Paths;

    /// <summary>None: the library keeps no content hash (a transfer computes the other side's).</summary>
    public string? HashName => null;

    public async Task<TransferEntry?> GetAsync(string path, CancellationToken cancellationToken)
    {
        if (!TryResolve(path, out var full, out var relative) || !IsThere(full) || _access.IsProtected(full))
            return null;
        return await _access.MayAsync(relative, Rights.Browse) ? Entry(Info(full), relative) : null;
    }

    public async IAsyncEnumerable<TransferEntry> ListAsync(
        string folder,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var (full, relative) = await ReadableAsync(folder, isFolder: true);
        foreach (var info in LibraryDisk.Children(full))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Links are never followed (as copies of folders do), work folders never listed.
            if (info.LinkTarget is not null || _library.Work.Contains(info.FullName))
                continue;
            var path = LibraryClashes.Join(relative, info.Name);
            // A folder the person may not open is left out, as a copy leaves it out.
            if (info is DirectoryInfo && !await _access.MayAsync(path, Rights.ViewFiles))
                continue;
            yield return Entry(info, path);
        }
    }

    public async Task<Stream> OpenReadAsync(TransferEntry file, long offset, CancellationToken cancellationToken)
    {
        var (full, _) = await ReadableAsync(file.Path, isFolder: false);
        try
        {
            return LibraryDisk.OpenAt(full, offset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FromDisk(ex, $"“{file.Name}” could not be read");
        }
    }

    public async Task<ITransferWrite> BeginWriteAsync(
        string folder,
        string name,
        long size,
        DateTime? modified,
        CancellationToken cancellationToken
    )
    {
        var (folderFull, folderPath) = await WritableFolderAsync(folder);
        CheckName(folderPath, name);
        var destination = Path.Combine(folderFull, name);
        if (LibraryClashes.Taken(folderFull)(name))
            throw Exists(name);
        CheckRoom(folderFull, size);

        var hold = _library.Scans.TryBeginOperation(destination) ?? throw ScanRunning();
        try
        {
            var stream = CreatePartial(folderFull, name);
            return new LibraryTransferWrite(this, stream, hold, destination, modified);
        }
        catch
        {
            hold.Dispose();
            throw;
        }
    }

    public async Task<TransferEntry> CreateFolderAsync(string folder, string name, CancellationToken cancellationToken)
    {
        var (folderFull, folderPath) = await WritableFolderAsync(folder);
        CheckName(folderPath, name);
        var full = Path.Combine(folderFull, name);
        var path = LibraryClashes.Join(folderPath, name);
        if (Directory.Exists(full))
            return Entry(new DirectoryInfo(full), path);
        if (File.Exists(full))
            throw Exists(name);
        try
        {
            Directory.CreateDirectory(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FromDisk(ex, $"The folder “{name}” could not be created");
        }

        _library.Browser.Invalidate(folderFull);
        return Entry(new DirectoryInfo(full), path);
    }

    public Task<string> FreeNameAsync(string folder, string name, bool isFolder, CancellationToken cancellationToken)
    {
        if (!TryResolve(folder, out var folderFull, out var folderPath) || !Directory.Exists(folderFull))
            throw NotFound(folder);
        var exists = LibraryClashes.Taken(folderFull);
        // A name no route reaches (the work folder at the root) is taken too.
        bool taken(string candidate) =>
            exists(candidate) || !Paths.TryResolve(LibraryClashes.Join(folderPath, candidate), out _);
        if (!taken(name))
            return Task.FromResult(name);
        return Task.FromResult(FileNames.KeepBoth(name, isFolder, taken) ?? throw Exists(name));
    }

    public async Task RemoveForReplaceAsync(TransferEntry existing, CancellationToken cancellationToken)
    {
        var (full, path) = ResolveExisting(existing.Path);
        var isFolder = Directory.Exists(full);
        var replaceRight = _library.Trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles;
        if (_access.IsReadOnly(full) || !await MayAllAsync(path, isFolder, replaceRight))
            throw Refused(path);
        await ToTrashAsync(full, path, isFolder, cancellationToken);
    }

    public async Task DeleteSourceAsync(TransferEntry entry, CancellationToken cancellationToken)
    {
        var (full, path) = ResolveExisting(entry.Path);
        var isFolder = Directory.Exists(full);
        // Moving takes it away from its folder: the move right there too, as the library's own moves need.
        if (_access.IsReadOnly(full) || !await MayAllAsync(path, isFolder, _writeRight))
            throw Refused(path);
        await ToTrashAsync(full, path, isFolder, cancellationToken);
        _library.Logger.LogInformation("{Account} moved {Path} out of the library", _actor.Name, path);
    }

    public Task<TransferSpace> FreeSpaceAsync(string folder, CancellationToken cancellationToken)
    {
        var full = TryResolve(folder, out var resolved, out _) ? resolved : Paths.Root;
        return Task.FromResult(new TransferSpace(RoomLeft(full) is { } room ? Math.Max(0, room) : null));
    }

    public async Task AfterWriteAsync(TransferEntry written, CancellationToken cancellationToken)
    {
        if (written.IsFolder || !TryResolve(written.Path, out var full, out _) || !File.Exists(full))
            return;
        await TryIndexAsync(full, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Bytes the drive of <paramref name="full"/> takes above the minimum; null when unknown.</summary>
    internal long? RoomLeft(string full) => LibraryDisk.FreeSpace(full) is { } free ? free - MarginBytes : null;

    internal long MarginBytes => Math.Max(0, _library.Files.MinimumFreeSpaceMegabytes) * 1024L * 1024;

    /// <summary>A file just committed: its folder's listing is read again.</summary>
    internal TransferEntry Committed(string destination)
    {
        _library.Browser.Invalidate(Path.GetDirectoryName(destination)!);
        return Entry(new FileInfo(destination), Paths.ToRelative(destination)!);
    }

    /// <summary>Indexes a file that arrived (its folders' default tags apply); else the next scan does.</summary>
    private async Task TryIndexAsync(string full, CancellationToken cancellationToken)
    {
        try
        {
            await _library.Indexer.IndexFilesAsync([full], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _library.Logger.LogWarning(
                ex,
                "The transferred file {Path} could not be indexed: the next scan does",
                full
            );
        }
    }

    private bool TryResolve(string path, out string full, out string relative)
    {
        full = relative = string.Empty;
        if (!Paths.TryResolve(path, out var resolved) || Paths.ToRelative(resolved) is not { } canonical)
            return false;
        (full, relative) = (resolved, canonical);
        return true;
    }

    /// <summary>An item that is there and may be changed by the server (not the root).</summary>
    private (string Full, string Path) ResolveExisting(string path)
    {
        if (!TryResolve(path, out var full, out var relative) || relative.Length == 0 || !IsThere(full))
            throw NotFound(path);
        if (_access.IsProtected(full))
            throw NotFound(path);
        return (full, relative);
    }

    /// <summary>A file or folder whose content the person may read: <c>files.view</c> there.</summary>
    private async Task<(string Full, string Path)> ReadableAsync(string path, bool isFolder)
    {
        var found =
            TryResolve(path, out var full, out var relative)
            && (isFolder ? Directory.Exists(full) : File.Exists(full))
            && !_access.IsProtected(full)
            && await _access.MayAsync(relative, Rights.Browse);
        if (!found)
            throw NotFound(path);
        if (!await _access.MayAsync(relative, Rights.ViewFiles))
            throw Refused(relative);
        return (full, relative);
    }

    /// <summary>A folder the person may write into with the job's right, and the server too.</summary>
    private async Task<(string Full, string Path)> WritableFolderAsync(string folder)
    {
        if (!TryResolve(folder, out var full, out var relative) || !Directory.Exists(full))
            throw NotFound(folder);
        if (_access.IsReadOnly(full) || !await _access.MayAsync(relative, _writeRight))
            throw Refused(relative);
        return (full, relative);
    }

    /// <summary>The person may use <paramref name="right"/> at the item, its folder and all below it.</summary>
    private async Task<bool> MayAllAsync(string path, bool isFolder, string right) =>
        await _access.MayAsync(path, right)
        && await _access.MayAsync(Parent(path), right)
        && (!isFolder || await _access.MayThroughoutAsync(path, right));

    private void CheckName(string folderPath, string name)
    {
        if (FileNames.Check(name, _library.Files.PortableNames) is { } problem)
            throw InvalidName(problem);
        // The work folder's name at the root: kept for SFE.
        if (!Paths.TryResolve(LibraryClashes.Join(folderPath, name), out _))
            throw InvalidName($"“{name}” is kept for SFE's own files.");
    }

    private void CheckRoom(string folderFull, long size)
    {
        if (RoomLeft(folderFull) is { } room && room < size)
            throw NoRoom(MarginBytes);
    }

    /// <summary>A free <c>name.sfe-partial</c> next to where the file goes, created and told to the job.</summary>
    private FileStream CreatePartial(string folderFull, string name)
    {
        var stem = name.Length > 200 ? name[..200].TrimEnd('.', ' ') : name;
        var taken = LibraryClashes.Taken(folderFull);
        for (var n = 1; n <= FileNames.MaxKeepBothAttempts; n++)
        {
            var partial = n == 1 ? stem + FileJobs.PartialSuffix : $"{stem} ({n}){FileJobs.PartialSuffix}";
            if (taken(partial))
                continue;
            var full = Path.Combine(folderFull, partial);
            _partials.Add(full);
            return LibraryDisk.CreateFile(full, name);
        }
        throw Exists(name);
    }

    private async Task ToTrashAsync(string full, string path, bool isFolder, CancellationToken cancellationToken)
    {
        using var hold = _library.Scans.TryBeginOperation(full) ?? throw ScanRunning();
        try
        {
            await MoveToTrashAsync(full, path, isFolder, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FromDisk(ex, $"“{Path.GetFileName(full)}” could not be removed");
        }
        _library.Browser.Invalidate(Path.GetDirectoryName(full)!);
    }

    /// <summary>To the trash, or for good when the server deletes everything for good (as replacing does).</summary>
    private Task MoveToTrashAsync(string full, string path, bool isFolder, CancellationToken cancellationToken) =>
        _library.Trash.AlwaysPermanent
            ? _library.Trash.DeleteForGoodAsync(full, path, isFolder, _actor, cancellationToken)
            : _library.Trash.MoveInAsync(full, path, isFolder, _actor, cancellationToken);

    private TransferEntry Entry(FileSystemInfo info, string path) =>
        new(
            path.Length == 0 ? Paths.RootName : info.Name,
            path,
            info is DirectoryInfo,
            info is FileInfo file ? file.Length : null,
            info.LastWriteTimeUtc
        );

    private static FileSystemInfo Info(string full) =>
        Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);

    private static bool IsThere(string full) => File.Exists(full) || Directory.Exists(full);

    private static string Parent(string path) =>
        path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;
}
