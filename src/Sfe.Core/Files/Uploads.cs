using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Files;

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
    UploadRecords records,
    UploadPlaces places,
    LibraryBrowser browser,
    DirectoryIndexer indexer,
    ScanCoordinator scans,
    Trash trash,
    IOptionsMonitor<SfeOptions> options,
    ILogger<Uploads> logger
)
{
    public const string FolderName = UploadRecords.FolderName;

    /// <summary>
    /// The largest chunk one request may carry (under Kestrel's 30 MB and most proxies' limits once lowered).
    /// </summary>
    public const int MaxChunkBytes = 16 * 1024 * 1024;

    /// <summary>Unfinished uploads one person may have at once.</summary>
    public const int MaxPerOwner = 200;

    /// <summary>Folders a folder upload may name on the way to its file.</summary>
    public const int MaxDepth = UploadPlaces.MaxDepth;

    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private DateTimeOffset _lastSweep;

    private FileManagementOptions Settings => options.CurrentValue.Files;

    /// <summary>Checks and starts an upload; nothing is written but its record.</summary>
    public async Task<UploadChunkResult> StartAsync(
        UploadStart start,
        FileActor actor,
        IUploadAccess access,
        CancellationToken cancellationToken = default
    )
    {
        Sweep();
        if (await places.CheckAsync(start.Target, start.Name, access) is { Error: { } error })
            return new UploadChunkResult(error.Status, null, error.Message);
        if (start.Size < 0)
            return new UploadChunkResult(FileOperationStatus.Invalid, null, "The size is not valid.");
        var name = Base(start.Name!);
        if (Settings.Uploads.MaxFileSizeMegabytes is > 0 and var max && start.Size > max * 1024L * 1024)
        {
            return new UploadChunkResult(
                FileOperationStatus.Invalid,
                null,
                $"“{name}” is larger than the {max:N0} MB a file may have here."
            );
        }

        if (FreeSpace(records.Folder) is { } free && free - start.Size < Margin)
        {
            return new UploadChunkResult(
                FileOperationStatus.Failed,
                null,
                $"“{name}” can't be uploaded: the drive would keep less than {Margin / 1024 / 1024:N0} MB free."
            );
        }

        var all = records.All();
        if (all.Values.Count(u => u.OwnerId == actor.AccountId) >= MaxPerOwner)
        {
            return new UploadChunkResult(
                FileOperationStatus.Conflict,
                null,
                $"You have {MaxPerOwner} unfinished uploads: finish or cancel some first."
            );
        }

        var now = DateTimeOffset.UtcNow;
        if (!paths.TryResolve(start.Target, out var targetFull))
            return new UploadChunkResult(FileOperationStatus.NotFound, null, "The folder was not found.");
        var upload = new UploadInfo(
            Guid.NewGuid().ToString("N"),
            paths.ToRelative(targetFull)!,
            Clean(start.Name!),
            start.Size,
            0,
            start.Modified,
            actor.AccountId,
            actor.Name,
            now,
            now
        );
        await using (File.Create(PartPath(upload.Id))) { }
        records.Save(upload);
        logger.LogInformation(
            "{Account} started uploading {Name} ({Size} bytes) to {Target}",
            actor.Name,
            upload.Name,
            upload.Size,
            Display(upload.Target)
        );
        return new UploadChunkResult(FileOperationStatus.Done, upload);
    }

    /// <summary>The unfinished uploads of <paramref name="ownerId"/> (all of them with null), oldest first.</summary>
    public IReadOnlyList<UploadInfo> List(int? ownerId, bool all = false)
    {
        Sweep();
        return [.. records.All().Values.Where(u => all || u.OwnerId == ownerId).OrderBy(u => u.Started)];
    }

    public UploadInfo? Find(string? id) => records.Find(id);

    /// <summary>
    /// Appends <paramref name="body"/> at <paramref name="offset"/>: refused
    /// (<see cref="FileOperationStatus.Conflict"/>, with the upload) when the server has another offset, or when it
    /// would pass the size; dropped (<see cref="UploadChunkResult.Damaged"/>) when its bytes don't match
    /// <paramref name="sha256"/> (<see cref="ChunkDigest"/>).
    /// </summary>
    public async Task<UploadChunkResult> AppendAsync(
        string id,
        long offset,
        Stream body,
        CancellationToken cancellationToken = default,
        byte[]? sha256 = null
    )
    {
        var gate = _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(id) is not { } upload || !File.Exists(PartPath(id)))
            {
                return new UploadChunkResult(
                    FileOperationStatus.NotFound,
                    null,
                    "This upload is not there any more: start it again."
                );
            }

            if (offset != upload.Received)
            {
                return new UploadChunkResult(
                    FileOperationStatus.Conflict,
                    upload,
                    $"The server has {upload.Received} bytes of it."
                );
            }

            var (received, refused) = await WriteAsync(upload, body, sha256, cancellationToken);
            if (refused is not null)
                return refused;
            upload = upload with { Received = received, LastActivity = DateTimeOffset.UtcNow };
            records.Save(upload);
            return new UploadChunkResult(FileOperationStatus.Done, upload);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Puts a fully received upload into the library: the folders of a folder upload are made, a name taken is answered
    /// as <paramref name="conflicts"/> says (<see cref="ConflictChoice.Ask"/>:
    /// <see cref="FileOperationStatus.Exists"/>, to ask the person and finish again), the file is indexed. A skipped
    /// upload is removed.
    /// </summary>
    public async Task<FileOperationResult> FinishAsync(
        string id,
        ConflictChoice conflicts,
        FileActor actor,
        IUploadAccess access,
        CancellationToken cancellationToken = default
    )
    {
        var gate = _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(id) is not { } upload || !File.Exists(PartPath(id)))
            {
                return FileOperationResult.Fail(
                    FileOperationStatus.NotFound,
                    "This upload is not there any more: start it again."
                );
            }

            if (upload.Received != upload.Size || new FileInfo(PartPath(id)).Length != upload.Size)
            {
                return FileOperationResult.Fail(
                    FileOperationStatus.Invalid,
                    $"“{Base(upload.Name)}” is not fully uploaded yet."
                );
            }
            // The person's access now (it may have changed since the start), and the place still there.
            if (await places.CheckAsync(upload.Target, upload.Name, access) is { Error: { } error })
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
                {
                    return FileOperationResult.Fail(
                        FileOperationStatus.Exists,
                        $"There is a file named “{segment}” where its folder would go."
                    );
                }

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
                        return FileOperationResult.Fail(
                            FileOperationStatus.Skipped,
                            $"“{name}” was left out: there is one of that name there."
                        );
                    case ConflictChoice.KeepBoth:
                        name = FileNames.KeepBoth(name, false, n =>
                            File.Exists(Path.Join(folder, n)) || Directory.Exists(Path.Join(folder, n))
                        ) ?? throw new IOException("No free name.");
                        destination = Path.Join(folder, name);
                        break;
                    case ConflictChoice.Replace when File.Exists(destination):
                        var right = trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles;
                        if (!await access.MayAsync(existingPath, right))
                        {
                            return FileOperationResult.Fail(
                                FileOperationStatus.Forbidden,
                                $"You may not replace “{name}” there."
                            );
                        }

                        using (var hold = scans.TryBeginOperation(destination))
                        {
                            if (hold is null)
                                return ScanRunning();
                            if (trash.AlwaysPermanent)
                            {
                                await trash.DeleteForGoodAsync(
                                    destination,
                                    existingPath,
                                    false,
                                    actor,
                                    cancellationToken
                                );
                            }
                            else
                            {
                                await trash.MoveInAsync(destination, existingPath, false, actor, cancellationToken);
                            }
                        }
                        break;
                    default:
                        return FileOperationResult.Fail(
                            FileOperationStatus.Exists,
                            $"There is already a file or folder named “{name}” here."
                        );
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
                    logger.LogWarning(
                        ex,
                        "{Account} could not finish uploading {Name} to {Target}",
                        actor.Name,
                        upload.Name,
                        Display(upload.Target)
                    );
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
        logger.LogInformation(
            "{Account} cancelled the upload of {Name} to {Target}",
            actor.Name,
            upload.Name,
            Display(upload.Target)
        );
        return true;
    }

    /// <summary>
    /// Removes the uploads idle for longer than <c>Files:Uploads:KeepUnfinishedHours</c> (at <paramref name="now"/>);
    /// how many. An upload busy with a chunk or a finish is left alone (its sender came back).
    /// </summary>
    public int RemoveAbandoned(DateTimeOffset? now = null)
    {
        var limit =
            (now ?? DateTimeOffset.UtcNow) - TimeSpan.FromHours(Math.Max(1, Settings.Uploads.KeepUnfinishedHours));
        var removed = 0;
        foreach (var idle in records.All().Values.Where(u => u.LastActivity < limit).ToList())
        {
            var gate = _gates.GetOrAdd(idle.Id, _ => new SemaphoreSlim(1, 1));
            if (!gate.Wait(0))
                continue;
            try
            {
                // Read again under the gate: a chunk may have come since the list was made.
                if (Find(idle.Id) is { } upload && upload.LastActivity < limit)
                {
                    Remove(upload);
                    removed++;
                }
            }
            finally
            {
                gate.Release();
            }
        }
        if (removed > 0)
        {
            logger.LogInformation(
                "Removed {Count} uploads not finished for {Hours} hours",
                removed,
                Settings.Uploads.KeepUnfinishedHours
            );
        }

        return removed;
    }

    private async Task<string?> TooLongForTheIndexAsync(string full, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return db.PathLengthLimit is { } limit && full.Length > limit
            ? $"“{Path.GetFileName(full)}” can't go there: its full path would pass the {limit} characters the index "
                + $"can keep."
            : null;
    }

    private void Remove(UploadInfo upload)
    {
        records.Remove(upload);
        _gates.TryRemove(upload.Id, out _);
    }

    /// <summary>
    /// Writes a chunk after what the upload has, flushed to the disk: the bytes the upload has then, or why the chunk
    /// was refused (too large, past the size, not matching <paramref name="sha256"/>: nothing of it kept).
    /// </summary>
    private async Task<(long Received, UploadChunkResult? Refused)> WriteAsync(
        UploadInfo upload,
        Stream body,
        byte[]? sha256,
        CancellationToken cancellationToken
    )
    {
        var received = upload.Received;
        var buffer = new byte[81920];
        using var hash = sha256 is null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var part = new FileStream(
            PartPath(upload.Id),
            FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            1,
            FileOptions.Asynchronous
        );
        // Bytes past what was stored (a write cut off before the record was saved) are dropped.
        part.SetLength(received);
        part.Position = received;
        try
        {
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (received + read > upload.Size || received + read - upload.Received > MaxChunkBytes)
                {
                    part.SetLength(upload.Received);
                    return (upload.Received, TooMuch(upload, received + read - upload.Received > MaxChunkBytes));
                }
                hash?.AppendData(buffer, 0, read);
                await part.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
            }
            if (sha256 is not null && !ChunkDigest.Matches(sha256, hash!.GetHashAndReset()))
            {
                part.SetLength(upload.Received);
                logger.LogWarning("A chunk of the upload of {Name} was damaged on the way: asked again", upload.Name);
                var damaged = new UploadChunkResult(FileOperationStatus.Invalid, upload, ChunkDigest.Mismatch, true);
                return (upload.Received, damaged);
            }
            await part.FlushAsync(cancellationToken);
            part.Flush(flushToDisk: true);
            return (received, null);
        }
        catch
        {
            // A connection lost mid-chunk: only what was stored before it counts.
            part.SetLength(upload.Received);
            throw;
        }
    }

    private static UploadChunkResult TooMuch(UploadInfo upload, bool chunkTooLarge) =>
        new(
            FileOperationStatus.Invalid,
            upload,
            chunkTooLarge
                ? $"A chunk may carry {MaxChunkBytes / 1024 / 1024} MB at most."
                : "This is more bytes than the size given at the start."
        );

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

    private string PartPath(string id) => records.PartPath(id);

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
        FileOperationResult.Fail(
            FileOperationStatus.ScanRunning,
            "A scan is running over this folder: try again when it has finished."
        );

    private static string Clean(string name) => string.Join('/', name.Replace('\\', '/').Split('/'));

    private static string Base(string name) => name.Replace('\\', '/').Split('/')[^1];

    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    private string Display(string relative) => relative.Length == 0 ? paths.RootName : relative;
}
