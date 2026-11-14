using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Files;

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

    /// <summary>
    /// Counts the folder <paramref name="fullPath"/> (root-relative <paramref name="path"/>) on the disk.
    /// </summary>
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
                await using var input = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    ReadBuffer,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                );
                job.Total = input.Length;
                using var hash = IncrementalHash.CreateHash(
                    algorithm switch
                    {
                        ChecksumAlgorithm.Sha1 => HashAlgorithmName.SHA1,
                        ChecksumAlgorithm.Md5 => HashAlgorithmName.MD5,
                        _ => HashAlgorithmName.SHA256,
                    }
                );
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
            catch (ObjectDisposedException) { }
        }

        public PropertyJobView View() =>
            new(
                Id,
                Kind,
                path,
                algorithm,
                Ended is not null,
                Interlocked.Read(ref Files),
                Interlocked.Read(ref Folders),
                Interlocked.Read(ref Bytes),
                Total,
                Interlocked.Read(ref LeftOut),
                Capped,
                Checksum,
                Error
            );
    }
}
