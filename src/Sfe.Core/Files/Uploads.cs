using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Files;

/// <param name="Target">The folder it goes into (root-relative).</param>
/// <param name="Name">
/// The file's name, or its path below <paramref name="Target"/> for a folder upload (<c>Trip/Day 1/a.jpg</c>): the
/// folders on the way are created when it is finished.
/// </param>
/// <param name="Modified">The file's modification time on the uploader's computer, kept on the uploaded file.</param>
public sealed record UploadStart(string? Target, string? Name, long Size, DateTimeOffset? Modified = null);

/// <summary>An upload not finished yet: what the uploader's browser needs to go on (also after a reload).</summary>
/// <param name="Received">Bytes stored so far: the next chunk starts there.</param>
public sealed record UploadInfo(
    string Id, string Target, string Name, long Size, long Received, DateTimeOffset? Modified, int? OwnerId, string OwnerName,
    DateTimeOffset Started, DateTimeOffset LastActivity);

/// <summary>What a chunk did: the upload as it is now, or why it was refused.</summary>
public sealed record UploadChunkResult(FileOperationStatus Status, UploadInfo? Upload, string? Message = null);

/// <summary>What the person may do, asked when an upload starts and again when it is finished.</summary>
public interface IUploadAccess
{
    /// <summary>The person may use <paramref name="right"/> at the root-relative <paramref name="path"/> (there or not yet).</summary>
    Task<bool> MayAsync(string path, string right);

    /// <summary>The full path really is (through links) among the server's own files or a work folder: never written to.</summary>
    bool IsProtected(string fullPath);
}

/// <summary>
/// Resumable uploads (agents/docs/features/uploads.md): a start answers an id, chunks are appended at the offset the
/// server has (a chunk at another offset is refused with the current one, so a browser that lost a reply resends from
/// there), and the finish moves the file into the library, asking about a name taken. Unfinished uploads live in the
/// work folder (<c>uploads/{id}.part</c> and its <c>{id}.json</c>), so they survive restarts and the uploader's reload;
/// those idle longer than <c>Files:Uploads:KeepUnfinishedHours</c> are removed.
/// </summary>
public sealed class Uploads(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    WorkFolder work,
    LibraryBrowser browser,
    DirectoryIndexer indexer,
    ScanCoordinator scans,
    Trash trash,
    IOptionsMonitor<SfeOptions> options,
    ILogger<Uploads> logger)
{
    public const string FolderName = "uploads";

    /// <summary>The largest chunk one request may carry (under Kestrel's 30 MB and most proxies' limits once lowered).</summary>
    public const int MaxChunkBytes = 16 * 1024 * 1024;

    /// <summary>Unfinished uploads one person may have at once.</summary>
    public const int MaxPerOwner = 200;

    /// <summary>Folders a folder upload may name on the way to its file.</summary>
    public const int MaxDepth = 32;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly Lock _loadGate = new();
    private ConcurrentDictionary<string, UploadInfo>? _uploads;
    private string? _loadedFrom;
    private DateTimeOffset _lastSweep;

    private FileManagementOptions Settings => options.CurrentValue.Files;

    /// <summary>Checks and starts an upload; nothing is written but its record.</summary>
    public async Task<UploadChunkResult> StartAsync(UploadStart start, FileActor actor, IUploadAccess access, CancellationToken cancellationToken = default)
    {
        Sweep();
        if (await PlaceAsync(start.Target, start.Name, access) is { Error: { } error })
            return new UploadChunkResult(error.Status, null, error.Message);
        if (start.Size < 0)
            return new UploadChunkResult(FileOperationStatus.Invalid, null, "The size is not valid.");
        var name = Base(start.Name!);
        if (Settings.Uploads.MaxFileSizeMegabytes is > 0 and var max && start.Size > max * 1024L * 1024)
            return new UploadChunkResult(FileOperationStatus.Invalid, null, $"“{name}” is larger than the {max:N0} MB a file may have here.");
        if (FreeSpace(work.Get(FolderName)) is { } free && free - start.Size < Margin)
            return new UploadChunkResult(FileOperationStatus.Failed, null, $"“{name}” can't be uploaded: the drive would keep less than {Margin / 1024 / 1024:N0} MB free.");
        var all = Load();
        if (all.Values.Count(u => u.OwnerId == actor.AccountId) >= MaxPerOwner)
            return new UploadChunkResult(FileOperationStatus.Conflict, null, $"You have {MaxPerOwner} unfinished uploads: finish or cancel some first.");

        var now = DateTimeOffset.UtcNow;
        if (!paths.TryResolve(start.Target, out var targetFull))
            return new UploadChunkResult(FileOperationStatus.NotFound, null, "The folder was not found.");
        var upload = new UploadInfo(Guid.NewGuid().ToString("N"), paths.ToRelative(targetFull)!, Clean(start.Name!), start.Size, 0,
            start.Modified, actor.AccountId, actor.Name, now, now);
        await using (File.Create(PartPath(upload.Id))) { }
        Save(upload);
        all[upload.Id] = upload;
        logger.LogInformation("{Account} started uploading {Name} ({Size} bytes) to {Target}", actor.Name, upload.Name, upload.Size, Display(upload.Target));
        return new UploadChunkResult(FileOperationStatus.Done, upload);
    }

    /// <summary>The unfinished uploads of <paramref name="ownerId"/> (all of them with null), oldest first.</summary>
    public IReadOnlyList<UploadInfo> List(int? ownerId, bool all = false)
    {
        Sweep();
        return [.. Load().Values.Where(u => all || u.OwnerId == ownerId).OrderBy(u => u.Started)];
    }

    public UploadInfo? Find(string? id) => id is not null && Load().TryGetValue(id, out var upload) ? upload : null;

    /// <summary>
    /// Appends <paramref name="body"/> at <paramref name="offset"/>: refused (<see cref="FileOperationStatus.Conflict"/>,
    /// with the upload) when the server has another offset, or when it would pass the size.
    /// </summary>
    public async Task<UploadChunkResult> AppendAsync(string id, long offset, Stream body, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(id) is not { } upload || !File.Exists(PartPath(id)))
                return new UploadChunkResult(FileOperationStatus.NotFound, null, "This upload is not there any more: start it again.");
            if (offset != upload.Received)
                return new UploadChunkResult(FileOperationStatus.Conflict, upload, $"The server has {upload.Received} bytes of it.");

            var received = upload.Received;
            var buffer = new byte[81920];
            await using (var part = new FileStream(PartPath(id), FileMode.Open, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous))
            {
                // Bytes past what was stored (a write cut off before the record was saved) are dropped.
                part.SetLength(received);
                part.Position = received;
                var chunk = 0;
                int read;
                try
                {
                    while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        chunk += read;
                        if (received + read > upload.Size || chunk > MaxChunkBytes)
                        {
                            part.SetLength(upload.Received);
                            return new UploadChunkResult(FileOperationStatus.Invalid, upload,
                                chunk > MaxChunkBytes ? $"A chunk may carry {MaxChunkBytes / 1024 / 1024} MB at most." : "More bytes than the size given at the start.");
                        }
                        await part.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        received += read;
                    }
                    await part.FlushAsync(cancellationToken);
                    part.Flush(flushToDisk: true);
                }
                catch
                {
                    // A connection lost mid-chunk: only what was stored before it counts.
                    part.SetLength(upload.Received);
                    throw;
                }
            }
            upload = upload with { Received = received, LastActivity = DateTimeOffset.UtcNow };
            Save(upload);
            Load()[id] = upload;
            return new UploadChunkResult(FileOperationStatus.Done, upload);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Puts a fully received upload into the library: the folders of a folder upload are made, a name taken is
    /// answered as <paramref name="conflicts"/> says (<see cref="ConflictChoice.Ask"/>: <see cref="FileOperationStatus.Exists"/>,
    /// to ask the person and finish again), the file is indexed. A skipped upload is removed.
    /// </summary>
    public async Task<FileOperationResult> FinishAsync(string id, ConflictChoice conflicts, FileActor actor, IUploadAccess access,
        CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(id) is not { } upload || !File.Exists(PartPath(id)))
                return FileOperationResult.Fail(FileOperationStatus.NotFound, "This upload is not there any more: start it again.");
            if (upload.Received != upload.Size || new FileInfo(PartPath(id)).Length != upload.Size)
                return FileOperationResult.Fail(FileOperationStatus.Invalid, $"“{Base(upload.Name)}” is not fully uploaded yet.");
            // The person's access now (it may have changed since the start), and the place still there.
            if (await PlaceAsync(upload.Target, upload.Name, access) is { Error: { } error })
                return error;

            var segments = upload.Name.Split('/');
            if (!paths.TryResolve(upload.Target, out var folder))
                return FileOperationResult.Fail(FileOperationStatus.NotFound, "The folder was not found.");
            var folderPath = paths.ToRelative(folder)!;
            var created = new List<string>();
            foreach (var segment in segments[..^1])
            {
                folder = Path.Join(folder, segment);
                folderPath = Join(folderPath, segment);
                if (File.Exists(folder))
                    return FileOperationResult.Fail(FileOperationStatus.Exists, $"There is a file named “{segment}” where its folder would go.");
                if (!Directory.Exists(folder))
                    created.Add(folder);
            }

            var name = segments[^1];
            var destination = Path.Join(folder, name);
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                var existingPath = Join(folderPath, name);
                switch (conflicts)
                {
                    case ConflictChoice.Skip:
                        Remove(upload);
                        return FileOperationResult.Fail(FileOperationStatus.Skipped, $"“{name}” was left out: there is one of that name there.");
                    case ConflictChoice.KeepBoth:
                        name = FileNames.KeepBoth(name, false, n => File.Exists(Path.Join(folder, n)) || Directory.Exists(Path.Join(folder, n)))
                            ?? throw new IOException("No free name.");
                        destination = Path.Join(folder, name);
                        break;
                    case ConflictChoice.Replace when File.Exists(destination):
                        var right = trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles;
                        if (!await access.MayAsync(existingPath, right))
                            return FileOperationResult.Fail(FileOperationStatus.Forbidden, $"You may not replace “{name}” there.");
                        using (var hold = scans.TryBeginOperation(destination))
                        {
                            if (hold is null)
                                return ScanRunning();
                            if (trash.AlwaysPermanent)
                                await trash.DeleteForGoodAsync(destination, existingPath, false, actor, cancellationToken);
                            else
                                await trash.MoveInAsync(destination, existingPath, false, actor, cancellationToken);
                        }
                        break;
                    default:
                        return FileOperationResult.Fail(FileOperationStatus.Exists, $"There is already a file or folder named “{name}” here.");
                }
            }
            if (await TooLongForTheIndexAsync(destination, cancellationToken) is { } tooLong)
                return FileOperationResult.Fail(FileOperationStatus.Invalid, tooLong);

            using (var hold = scans.TryBeginOperation(destination))
            {
                if (hold is null)
                    return ScanRunning();
                try
                {
                    foreach (var made in created)
                        Directory.CreateDirectory(made);
                    File.Move(PartPath(id), destination, overwrite: false);
                    if (upload.Modified is { } modified)
                        File.SetLastWriteTimeUtc(destination, modified.UtcDateTime);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "{Account} could not finish uploading {Name} to {Target}", actor.Name, upload.Name, Display(upload.Target));
                    return FileOperations.DiskFailure(ex, $"“{name}” could not be saved");
                }
            }
            Remove(upload);

            try
            {
                // Indexed at once (with the folders made on the way), so that it shows as indexed and can be tagged.
                await indexer.IndexFilesAsync([destination], cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The upload {Path} could not be indexed: the next scan does", destination);
            }
            browser.Invalidate(Path.GetDirectoryName(destination)!);
            if (created.Count > 0 && Path.GetDirectoryName(created[0]) is { } top)
                browser.Invalidate(top);
            var item = browser.ToItem(new FileInfo(destination))!;
            logger.LogInformation("{Account} uploaded {Path} ({Size} bytes)", actor.Name, item.Path, upload.Size);
            return new FileOperationResult(FileOperationStatus.Done, item);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Removes an unfinished upload and what it received.</summary>
    public bool Cancel(string id, FileActor actor)
    {
        if (Find(id) is not { } upload)
            return false;
        Remove(upload);
        logger.LogInformation("{Account} cancelled the upload of {Name} to {Target}", actor.Name, upload.Name, Display(upload.Target));
        return true;
    }

    /// <summary>Removes the uploads idle for longer than <c>Files:Uploads:KeepUnfinishedHours</c> (at <paramref name="now"/>); how many.</summary>
    public int RemoveAbandoned(DateTimeOffset? now = null)
    {
        var limit = (now ?? DateTimeOffset.UtcNow) - TimeSpan.FromHours(Math.Max(1, Settings.Uploads.KeepUnfinishedHours));
        var removed = 0;
        foreach (var upload in Load().Values.Where(u => u.LastActivity < limit).ToList())
        {
            Remove(upload);
            removed++;
        }
        if (removed > 0)
            logger.LogInformation("Removed {Count} uploads not finished for {Hours} hours", removed, Settings.Uploads.KeepUnfinishedHours);
        return removed;
    }

    /// <summary>
    /// Why <paramref name="name"/> can't be uploaded into <paramref name="target"/> by the person: the folder, each
    /// folder on the way and the name are checked (names as new names, no <c>..</c>, the work folder refused).
    /// </summary>
    private async Task<(FileOperationResult? Error, string? Path)> PlaceAsync(string? target, string? name, IUploadAccess access)
    {
        var notFound = (FileOperationResult.Fail(FileOperationStatus.NotFound, "The folder was not found."), (string?)null);
        if (LibraryPaths.NormalizeRelative(target) is not { } normalized || !paths.TryResolve(normalized, out var targetFull)
            || !Directory.Exists(targetFull) || access.IsProtected(targetFull) || paths.ToRelative(targetFull) is not { } targetPath)
            return notFound;
        if (!await access.MayAsync(targetPath, Rights.Browse))
            return notFound;
        if (string.IsNullOrEmpty(name))
            return (FileOperationResult.Fail(FileOperationStatus.Invalid, "A name is needed."), null);
        var segments = name.Replace('\\', '/').Split('/');
        if (segments.Length > MaxDepth)
            return (FileOperationResult.Fail(FileOperationStatus.Invalid, $"“{name}” is more than {MaxDepth} folders deep."), null);

        var path = targetPath;
        foreach (var segment in segments)
        {
            if (FileNames.Check(segment, Settings.PortableNames) is { } message)
                return (FileOperationResult.Fail(FileOperationStatus.Invalid, message), null);
            path = Join(path, segment);
            // SFE's work folder at the root, or a name the system would read as another one.
            if (!paths.TryResolve(path, out var full) || !LibraryPaths.PathComparer.Equals(Path.GetFileName(full), segment) || access.IsProtected(full))
                return (FileOperationResult.Fail(FileOperationStatus.Invalid, $"“{segment}” is reserved for SFE's own files: pick another name."), null);
        }
        // Each folder made on the way, and the folder the file lands in, needs the right to upload there.
        var place = targetPath;
        if (!await access.MayAsync(place, Rights.UploadFiles))
            return (FileOperationResult.Fail(FileOperationStatus.Forbidden, "You may not upload files there."), null);
        foreach (var segment in segments[..^1])
        {
            place = Join(place, segment);
            if (!await access.MayAsync(place, Rights.UploadFiles))
                return (FileOperationResult.Fail(FileOperationStatus.Forbidden, $"You may not upload files into “{segment}”."), null);
        }
        return (null, path);
    }

    private async Task<string?> TooLongForTheIndexAsync(string full, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return db.PathLengthLimit is { } limit && full.Length > limit
            ? $"“{Path.GetFileName(full)}” can't go there: its full path would pass the {limit} characters the index can keep."
            : null;
    }

    /// <summary>The uploads recorded in the work folder, read once (again when the work folder moves).</summary>
    private ConcurrentDictionary<string, UploadInfo> Load()
    {
        var folder = work.Get(FolderName);
        lock (_loadGate)
        {
            if (_uploads is not null && _loadedFrom == folder)
                return _uploads;
            var uploads = new ConcurrentDictionary<string, UploadInfo>();
            foreach (var record in Directory.EnumerateFiles(folder, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<UploadInfo>(File.ReadAllText(record), Json) is { } upload && upload.Id == Path.GetFileNameWithoutExtension(record))
                        uploads[upload.Id] = upload;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not read the upload record {File}", Path.GetFileName(record));
                }
            }
            _loadedFrom = folder;
            return _uploads = uploads;
        }
    }

    private void Save(UploadInfo upload)
    {
        var record = RecordPath(upload.Id);
        var temporary = record + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(upload, Json));
        File.Move(temporary, record, overwrite: true);
    }

    private void Remove(UploadInfo upload)
    {
        Load().TryRemove(upload.Id, out _);
        _gates.TryRemove(upload.Id, out _);
        foreach (var file in new[] { PartPath(upload.Id), RecordPath(upload.Id) })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not remove {File}", Path.GetFileName(file));
            }
        }
    }

    /// <summary>Removes abandoned uploads, at most every few minutes (when uploads are started or listed).</summary>
    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSweep < SweepEvery)
            return;
        _lastSweep = now;
        RemoveAbandoned();
    }

    private long Margin => Math.Max(0, Settings.MinimumFreeSpaceMegabytes) * 1024L * 1024;

    private string PartPath(string id) => Path.Combine(work.Get(FolderName), $"{SafeId(id)}.part");
    private string RecordPath(string id) => Path.Combine(work.Get(FolderName), $"{SafeId(id)}.json");

    /// <summary>Ids are 32 hexadecimal digits: anything else never names a file.</summary>
    private static string SafeId(string id) =>
        id.Length == 32 && id.All(Uri.IsHexDigit) ? id : throw new ArgumentException("Not an upload id.", nameof(id));

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

    private static FileOperationResult ScanRunning() =>
        FileOperationResult.Fail(FileOperationStatus.ScanRunning, "A scan is running over this folder: try again when it has finished.");

    private static string Clean(string name) => string.Join('/', name.Replace('\\', '/').Split('/'));
    private static string Base(string name) => name.Replace('\\', '/').Split('/')[^1];
    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";
    private string Display(string relative) => relative.Length == 0 ? paths.RootName : relative;
}
