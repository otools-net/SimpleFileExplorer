using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Paths;
using Sfe.Core.Search;

namespace Sfe.Core.Files;

/// <summary>What the disk says of a file or folder, beyond what a listing shows.</summary>
/// <param name="Hidden">Hidden by the system: a name starting with a dot, or the hidden attribute (Windows).</param>
/// <param name="IsLink">A symbolic link or junction (its target is not told: it may be outside the library).</param>
public sealed record ItemDetails(
    string Path, string Name, bool IsFolder, long? Size, DateTimeOffset? Created, DateTimeOffset Modified, DateTimeOffset? Accessed,
    bool Hidden, bool ReadOnly, bool IsLink);

/// <summary>What the index knows below a folder (the files and folders the asker may see).</summary>
/// <param name="Indexed">The folder itself is in the index (else the counts are those of what was indexed below it, if anything).</param>
public sealed record FolderTotals(bool Indexed, long Files, long Folders, long Bytes);

public enum PropertyJobKind
{
    /// <summary>Counts a folder's files, folders and bytes on the disk.</summary>
    Count,

    /// <summary>Reads a file whole for its checksum.</summary>
    Checksum,
}

public enum ChecksumAlgorithm
{
    Sha256,
    Sha1,
    Md5,
}

/// <param name="Bytes">Counted (a count), or read so far (a checksum).</param>
/// <param name="Total">The file's size, for a checksum's progress.</param>
/// <param name="LeftOut">Links not followed and folders that couldn't be read, while counting.</param>
/// <param name="Capped">The count stopped at <see cref="PropertyJobs.MaxCountedEntries"/>: the folder holds more.</param>
/// <param name="Checksum">The checksum, in lowercase hex, once done.</param>
public sealed record PropertyJobView(
    string Id, PropertyJobKind Kind, string Path, ChecksumAlgorithm? Algorithm, bool Done, long Files, long Folders, long Bytes, long? Total,
    long LeftOut, bool Capped, string? Checksum, string? Error);

/// <summary>The access of the person asking, while a folder is counted.</summary>
public interface IPropertyAccess
{
    /// <summary>The person sees the root-relative folder (folder rules): else it is left out without a word.</summary>
    bool Sees(string path);

    /// <summary>The full path, with no link on the way, is among the server's own files or a work folder.</summary>
    bool IsProtectedReal(string realPath);
}

/// <summary>
/// Properties of a file or folder (agents/docs/features/properties.md): the disk's details at once, a folder's totals
/// from the index, and what costs reading the disk (a folder counted, a file's checksum) as jobs that the asker
/// follows and can stop.
/// </summary>
public sealed class Properties(IDbContextFactory<SfeDbContext> dbFactory, LibraryPaths paths)
{
    /// <summary>The details of the root-relative path; null when it is neither a file nor a folder.</summary>
    public ItemDetails? Describe(string path, string fullPath)
    {
        FileSystemInfo info = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
        if (!info.Exists)
            return null;
        var name = path.Length == 0 ? paths.RootName : info.Name;
        return new ItemDetails(
            path, name, info is DirectoryInfo, (info as FileInfo)?.Length,
            Known(info.CreationTimeUtc), new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), Known(info.LastAccessTimeUtc),
            name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden), info.Attributes.HasFlag(FileAttributes.ReadOnly),
            info.LinkTarget is not null);
    }

    /// <summary>
    /// The files, folders and bytes the index has below <paramref name="fullPath"/>, only where
    /// <paramref name="visible"/> lets the asker see (folder rules).
    /// </summary>
    public async Task<FolderTotals> IndexedTotalsAsync(string fullPath, PathFilter? visible, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var self = Path.TrimEndingDirectorySeparator(fullPath);
        var indexed = await db.Directories.AnyAsync(d => d.FullName == self, cancellationToken);
        var files = db.Files.AsNoTracking().Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, fullPath)).WhereVisible(visible, f => f.FullName, paths);
        var folders = db.Directories.AsNoTracking().Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, fullPath))
            .Where(d => d.FullName != self).WhereVisible(visible, d => d.FullName, paths);
        return new FolderTotals(indexed, await files.LongCountAsync(cancellationToken), await folders.LongCountAsync(cancellationToken),
            await files.SumAsync(f => (long?)f.Size, cancellationToken) ?? 0);
    }

    /// <summary>A file system without the date answers 1601 (Windows) or 1970: told as unknown.</summary>
    private static DateTimeOffset? Known(DateTime utc) => utc.Year <= 1970 ? null : new DateTimeOffset(utc, TimeSpan.Zero);
}

/// <summary>
/// Folder counts and checksums, read in the background: each person (or visitor's address) has at most one of each
/// kind at once (a new one stops the previous), sees only their own, and a job is forgotten
/// <see cref="Keep"/> after it ended.
/// </summary>
public sealed class PropertyJobs(ILogger<PropertyJobs> logger)
{
    /// <summary>Files and folders one count goes through at most.</summary>
    public const int MaxCountedEntries = 2_000_000;

    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(10);

    private const int ReadBuffer = 1024 * 1024;

    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    /// <summary>Counts the folder <paramref name="fullPath"/> (root-relative <paramref name="path"/>) on the disk.</summary>
    public PropertyJobView StartCount(string owner, string path, string fullPath, IPropertyAccess access)
    {
        var job = Begin(owner, PropertyJobKind.Count, path, null);
        _ = Task.Run(() =>
        {
            try
            {
                Count(job, fullPath, LibraryPaths.RealPath(fullPath), path, access);
            }
            catch (OperationCanceledException)
            {
                job.Error = "Stopped.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Cannot count {Path}", path);
                job.Error = "The folder could not be read.";
            }
            finally
            {
                job.End();
            }
        });
        return job.View();
    }

    /// <summary>Reads the file <paramref name="fullPath"/> whole for its checksum.</summary>
    public PropertyJobView StartChecksum(string owner, string path, string fullPath, ChecksumAlgorithm algorithm)
    {
        var job = Begin(owner, PropertyJobKind.Checksum, path, algorithm);
        _ = Task.Run(async () =>
        {
            try
            {
                await using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, ReadBuffer,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                job.Total = input.Length;
                using var hash = IncrementalHash.CreateHash(algorithm switch
                {
                    ChecksumAlgorithm.Sha1 => HashAlgorithmName.SHA1,
                    ChecksumAlgorithm.Md5 => HashAlgorithmName.MD5,
                    _ => HashAlgorithmName.SHA256,
                });
                var buffer = new byte[ReadBuffer];
                int read;
                while ((read = await input.ReadAsync(buffer, job.Token)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    Interlocked.Add(ref job.Bytes, read);
                }
                job.Checksum = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            catch (OperationCanceledException)
            {
                job.Error = "Stopped.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Cannot read {Path} for its checksum", path);
                job.Error = "The file could not be read: another program may have it open.";
            }
            finally
            {
                job.End();
            }
        });
        return job.View();
    }

    /// <summary>The job when it is <paramref name="owner"/>'s.</summary>
    public PropertyJobView? Get(string owner, string id)
    {
        Forget();
        return _jobs.TryGetValue(id, out var job) && job.Owner == owner ? job.View() : null;
    }

    /// <summary>Stops the job when it is <paramref name="owner"/>'s; false when there is none.</summary>
    public bool Cancel(string owner, string id)
    {
        if (!_jobs.TryGetValue(id, out var job) || job.Owner != owner)
            return false;
        job.Stop();
        return true;
    }

    private Job Begin(string owner, PropertyJobKind kind, string path, ChecksumAlgorithm? algorithm)
    {
        Forget();
        foreach (var previous in _jobs.Values.Where(j => j.Owner == owner && j.Kind == kind))
        {
            previous.Stop();
            _jobs.TryRemove(previous.Id, out _);
        }
        var job = new Job(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), owner, kind, path, algorithm);
        _jobs[job.Id] = job;
        return job;
    }

    private void Forget()
    {
        var before = DateTimeOffset.UtcNow - Keep;
        foreach (var job in _jobs.Values.Where(j => j.Ended is { } ended && ended < before))
            _jobs.TryRemove(job.Id, out _);
    }

    /// <summary>Walks the folder without following links, nor entering the folders the asker doesn't see.</summary>
    private static void Count(Job job, string full, string real, string path, IPropertyAccess access)
    {
        var pending = new Stack<(string Full, string Real, string Path)>();
        pending.Push((full, real, path));
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        while (pending.TryPop(out var folder))
        {
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(folder.Full).EnumerateFileSystemInfos("*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref job.LeftOut);
                continue;
            }
            foreach (var entry in entries)
            {
                job.Token.ThrowIfCancellationRequested();
                if (job.Files + job.Folders >= MaxCountedEntries)
                {
                    job.Capped = true;
                    return;
                }
                var entryReal = Path.Join(folder.Real, entry.Name);
                if (access.IsProtectedReal(entryReal))
                    continue;
                if (entry.LinkTarget is not null)
                {
                    Interlocked.Increment(ref job.LeftOut);
                    continue;
                }
                var entryPath = folder.Path.Length == 0 ? entry.Name : $"{folder.Path}/{entry.Name}";
                if (entry is DirectoryInfo)
                {
                    if (!access.Sees(entryPath))
                        continue;
                    Interlocked.Increment(ref job.Folders);
                    pending.Push((entry.FullName, entryReal, entryPath));
                }
                else
                {
                    Interlocked.Increment(ref job.Files);
                    Interlocked.Add(ref job.Bytes, ((FileInfo)entry).Length);
                }
            }
        }
    }

    private sealed class Job(string id, string owner, PropertyJobKind kind, string path, ChecksumAlgorithm? algorithm)
    {
        private readonly CancellationTokenSource _cancel = new();

        public string Id { get; } = id;
        public string Owner { get; } = owner;
        public PropertyJobKind Kind { get; } = kind;
        public CancellationToken Token => _cancel.Token;
        public long Files;
        public long Folders;
        public long Bytes;
        public long LeftOut;
        public long? Total { get; set; }
        public volatile bool Capped;
        public string? Checksum { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset? Ended { get; private set; }

        public void End() => Ended = DateTimeOffset.UtcNow;

        public void Stop()
        {
            try
            {
                _cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public PropertyJobView View() => new(
            Id, Kind, path, algorithm, Ended is not null, Interlocked.Read(ref Files), Interlocked.Read(ref Folders), Interlocked.Read(ref Bytes), Total,
            Interlocked.Read(ref LeftOut), Capped, Checksum, Error);
    }
}
