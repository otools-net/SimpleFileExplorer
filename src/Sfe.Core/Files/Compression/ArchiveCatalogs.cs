using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Options;

namespace Sfe.Core.Files.Compression;

/// <summary>
/// How many archives' lists the whole server reads at once (<see cref="ArchiveOptions.CatalogBuilds"/>): one instance
/// for every library (as <see cref="Thumbnails.FfmpegLimits"/>), so that several libraries don't multiply the load.
/// </summary>
public sealed class ArchiveBuildGate
{
    private Gate _gate = new(1);
    private Gate _compress = new(1);

    /// <summary>The gate, resized to <paramref name="size"/> when the setting changed (builds running release the old one).</summary>
    public SemaphoreSlim Builds(int size) => Resized(ref _gate, size);

    /// <summary>The gate of compressions (zips written into libraries, <c>CompressJobs</c>), as <see cref="Builds"/>.</summary>
    public SemaphoreSlim Compressions(int size) => Resized(ref _compress, size);

    private static SemaphoreSlim Resized(ref Gate field, int size)
    {
        var gate = Volatile.Read(ref field);
        if (gate.Size != Math.Max(1, size))
        {
            Interlocked.CompareExchange(ref field, new Gate(size), gate);
            gate = Volatile.Read(ref field);
        }
        return gate.Semaphore;
    }

    private sealed class Gate(int size)
    {
        public int Size { get; } = Math.Max(1, size);
        public SemaphoreSlim Semaphore { get; } = new(Math.Max(1, size));
    }
}

public enum ArchiveCatalogStatus
{
    Ready,
    /// <summary>Being read (or waiting for its turn): <see cref="ArchiveCatalogState.Read"/> entries so far.</summary>
    Building,
    /// <summary>Couldn't be read (the disk, the file in use): <see cref="ArchiveCatalogState.Error"/> says so; asked again later.</summary>
    Failed,
    /// <summary>Not read yet, and no new read was started for this caller (<c>mayStart</c> said no): ask again later.</summary>
    Limited,
}

public sealed record ArchiveCatalogState(ArchiveCatalogStatus Status, ArchiveCatalog? Catalog = null, long Read = 0, string? Error = null);

/// <summary>
/// A library's archive catalogs (agents/docs/features/archive-reading.md): kept in its cache folder, beside the
/// thumbnails, one per archive and version (path, size, time); built in the background, one build per archive
/// however many ask, queued past <see cref="ArchiveOptions.CatalogBuilds"/>; a build nobody asks about for a minute
/// is dropped; past <see cref="ArchiveOptions.CatalogMegabytes"/> the least recently used catalogs are deleted.
/// </summary>
public sealed class ArchiveCatalogs(IOptionsMonitor<SfeOptions> options, ArchiveBuildGate gate, ILogger<ArchiveCatalogs> logger)
{
    public const string FolderName = "archives";

    /// <summary>The layout's folder: a new schema gets a new one, and the old ones are deleted.</summary>
    public static readonly string VersionFolder = $"v{ArchiveCatalog.Version}";

    /// <summary>A build no one asked about for this long is dropped (the viewer closed); shorter in tests.</summary>
    internal TimeSpan Abandoned { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>How long a failure is answered before the catalog is tried again.</summary>
    private static readonly TimeSpan FailureKept = TimeSpan.FromMinutes(1);

    /// <summary>A catalog's time is its last use (for eviction), written at most this often.</summary>
    private static readonly TimeSpan TouchEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Build> _builds = new();
    private int _cleaned;

    public string Folder => Path.Combine(options.CurrentValue.Thumbnails.CacheDirectory, FolderName, VersionFolder);

    /// <summary>The catalog's name: the archive's path and version. A changed archive is another catalog; the old one ages out.</summary>
    public static string KeyOf(string canonicalPath, long size, DateTime modifiedUtc) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{canonicalPath}\n{size}\n{modifiedUtc.Ticks}")))[..32];

    /// <summary>
    /// The catalog of the archive at <paramref name="fullPath"/> when it is ready; otherwise its build, started now if
    /// none runs (asking keeps a build alive). <paramref name="mayStart"/> is asked only before a new build starts (the
    /// caller's limit): no, and the answer is <see cref="ArchiveCatalogStatus.Limited"/>.
    /// </summary>
    public ArchiveCatalogState Get(string fullPath, string canonicalPath, ArchiveFormat format, Func<bool>? mayStart = null)
    {
        var info = new FileInfo(fullPath);
        var key = KeyOf(canonicalPath, info.Length, info.LastWriteTimeUtc);
        var file = Path.Combine(Folder, key + ".db");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (_builds.TryGetValue(key, out var running))
            {
                running.Asked = DateTime.UtcNow;
                if (running.Error is { } error)
                {
                    if (DateTime.UtcNow - running.Finished < FailureKept)
                        return new ArchiveCatalogState(ArchiveCatalogStatus.Failed, Error: error);
                    _builds.TryRemove(new KeyValuePair<string, Build>(key, running));
                    continue;
                }
                if (!running.Done)
                    return new ArchiveCatalogState(ArchiveCatalogStatus.Building, Read: running.Read);
                _builds.TryRemove(new KeyValuePair<string, Build>(key, running));
            }
            if (ArchiveCatalog.IsCurrent(file))
            {
                Touch(file);
                return new ArchiveCatalogState(ArchiveCatalogStatus.Ready, new ArchiveCatalog(file));
            }
            if (mayStart is not null && !mayStart())
                return new ArchiveCatalogState(ArchiveCatalogStatus.Limited);
            var build = new Build();
            if (!_builds.TryAdd(key, build))
                continue;
            _ = Task.Run(() => RunAsync(build, key, fullPath, format, file));
            return new ArchiveCatalogState(ArchiveCatalogStatus.Building);
        }
        return new ArchiveCatalogState(ArchiveCatalogStatus.Building);
    }

    private async Task RunAsync(Build build, string key, string fullPath, ArchiveFormat format, string file)
    {
        using var cancel = new CancellationTokenSource();
        var semaphore = gate.Builds(options.CurrentValue.Files.Archives.CatalogBuilds);
        var entered = false;
        try
        {
            // Waits for its turn, and gives it up when nobody asks any more.
            var poll = Abandoned < TimeSpan.FromSeconds(4) ? Abandoned / 2 : TimeSpan.FromSeconds(2);
            while (!(entered = await semaphore.WaitAsync(poll)))
                if (DateTime.UtcNow - build.Asked > Abandoned)
                    return;
            DeleteOldVersions();
            var progress = new Progress(read =>
            {
                build.Read = read;
                if (DateTime.UtcNow - build.Asked > Abandoned)
                    cancel.Cancel();
            });
            ArchiveCatalog.Build(fullPath, format, file, options.CurrentValue.Files.Archives, progress, cancel.Token);
            build.Done = true;
            Evict(file);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Stopped reading the list of {Archive}: nobody is looking at it any more.", fullPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        {
            logger.LogWarning(error, "Couldn't read the list of {Archive}.", fullPath);
            build.Error = "The archive can't be read now (in use, or the server may not read it).";
            build.Finished = DateTime.UtcNow;
        }
        finally
        {
            if (entered)
                semaphore.Release();
            // Done, dropped or cancelled: the next question finds the catalog, or starts again. A failure stays a while.
            if (build.Error is null)
                _builds.TryRemove(new KeyValuePair<string, Build>(key, build));
        }
    }

    /// <summary>Lists of archives with encrypted names held at once per library (each up to <see cref="ArchiveOptions.MaxEncryptedListEntries"/>).</summary>
    public const int MaxInMemory = 4;

    /// <summary>A list in memory unused this long is dropped (as the unlock that opened it).</summary>
    internal TimeSpan InMemoryIdle { get; init; } = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, MemoryList> _inMemory = [];

    /// <summary>
    /// The catalog of an archive whose names are encrypted, read with <paramref name="password"/> into memory for the
    /// unlock <paramref name="unlock"/>: built in the background as others are (Building), then Ready; Failed with a
    /// wrong password. Never written to disk.
    /// </summary>
    public ArchiveCatalogState GetInMemory(string fullPath, string canonicalPath, ArchiveFormat format, string unlock, string password)
    {
        var info = new FileInfo(fullPath);
        var key = unlock + ":" + KeyOf(canonicalPath, info.Length, info.LastWriteTimeUtc);
        MemoryList list;
        lock (_inMemory)
        {
            DropIdle();
            if (!_inMemory.TryGetValue(key, out list!))
            {
                while (_inMemory.Count >= MaxInMemory)
                    Drop(_inMemory.MinBy(pair => pair.Value.Used).Key);
                list = _inMemory[key] = new MemoryList();
                _ = Task.Run(() => BuildInMemoryAsync(list, fullPath, format, password));
            }
            list.Used = DateTime.UtcNow;
        }
        if (list.Error is { } error)
            return new ArchiveCatalogState(ArchiveCatalogStatus.Failed, Error: error);
        return list.Catalog is { } catalog
            ? new ArchiveCatalogState(ArchiveCatalogStatus.Ready, catalog)
            : new ArchiveCatalogState(ArchiveCatalogStatus.Building, Read: list.Read);
    }

    private async Task BuildInMemoryAsync(MemoryList list, string fullPath, ArchiveFormat format, string password)
    {
        var settings = options.CurrentValue.Files.Archives;
        var semaphore = gate.Builds(settings.CatalogBuilds);
        var entered = false;
        try
        {
            await semaphore.WaitAsync(list.Cancel.Token).ConfigureAwait(false);
            entered = true;
            var limited = new ArchiveOptions
            {
                MaxEntries = Math.Min(settings.MaxEntries, settings.MaxEncryptedListEntries),
                MaxDepth = settings.MaxDepth,
            };
            var catalog = ArchiveCatalog.BuildInMemory(fullPath, format, password, limited, out var keeper,
                new Progress(read => list.Read = read), list.Cancel.Token);
            lock (_inMemory)
            {
                if (list.Cancel.IsCancellationRequested)
                {
                    keeper.Dispose();
                    return;
                }
                list.Keeper = keeper;
                var summary = catalog.Summary();
                if (summary.NeedsPassword && summary.Entries == 0)
                    list.Error = "That password doesn't open this archive.";
                else
                    list.Catalog = catalog;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        {
            logger.LogWarning(error, "Couldn't read the list of {Archive} with its password.", fullPath);
            list.Error = "The archive can't be read now (in use, or the server may not read it).";
        }
        finally
        {
            if (entered)
                semaphore.Release();
        }
    }

    private void DropIdle()
    {
        foreach (var key in _inMemory.Where(pair => DateTime.UtcNow - pair.Value.Used > InMemoryIdle).Select(pair => pair.Key).ToList())
            Drop(key);
    }

    private void Drop(string key)
    {
        if (!_inMemory.Remove(key, out var list))
            return;
        list.Cancel.Cancel();
        list.Keeper?.Dispose();
    }

    private sealed class MemoryList
    {
        public readonly CancellationTokenSource Cancel = new();
        public DateTime Used = DateTime.UtcNow;
        public long Read;
        public ArchiveCatalog? Catalog;
        public SqliteConnection? Keeper;
        public string? Error;
    }

    /// <summary>The least recently used catalogs deleted until the library's are under the cap (the new one kept).</summary>
    internal void Evict(string keep)
    {
        var cap = Math.Max(1, options.CurrentValue.Files.Archives.CatalogMegabytes) * 1024L * 1024;
        var catalogs = new DirectoryInfo(Folder).EnumerateFiles("*.db").ToList();
        var total = catalogs.Sum(catalog => catalog.Length);
        foreach (var catalog in catalogs.OrderBy(catalog => catalog.LastWriteTimeUtc))
        {
            if (total <= cap)
                break;
            if (string.Equals(catalog.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var length = catalog.Length;
                catalog.Delete();
                total -= length;
            }
            catch (IOException)
            {
                // Being read right now: the next eviction takes it.
            }
        }
    }

    private static void Touch(string file)
    {
        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TouchEvery)
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Catalogs of another schema are never read: their folders go at the first build after an upgrade.</summary>
    private void DeleteOldVersions()
    {
        if (Interlocked.Exchange(ref _cleaned, 1) == 1)
            return;
        var root = new DirectoryInfo(Path.GetDirectoryName(Folder)!);
        if (!root.Exists)
            return;
        foreach (var folder in root.EnumerateDirectories().Where(folder => folder.Name != VersionFolder))
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class Build
    {
        public volatile bool Done;
        public long Read;
        public DateTime Asked = DateTime.UtcNow;
        public string? Error;
        public DateTime Finished;
    }

    private sealed class Progress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
