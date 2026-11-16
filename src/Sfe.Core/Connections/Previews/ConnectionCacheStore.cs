using System.Globalization;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Settings;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// The folder of the connections' cache (agents/docs/features/connection-previews.md): one for the whole server,
/// <c>{Thumbnails:CacheDirectory}/connections/{connection id}/{2 hex}/{key}{suffix}</c>, kept under its size and age
/// limits (<see cref="ConnectionCacheOptions"/>). The files used longest ago go first; a file being read
/// (<see cref="Hold"/>) or written (<c>.part</c>) stays. Never under a folder the app serves
/// (<see cref="IsServed"/>): the Api checks it at start and turns the cache off otherwise.
/// </summary>
public sealed class ConnectionCacheStore
{
    /// <summary>The suffix of a file being downloaded.</summary>
    public const string PartSuffix = ".part";

    /// <summary>Evicting goes down to this share of the limit, so the next files don't evict again at once.</summary>
    private const double EvictTo = 0.9;

    private static readonly TimeSpan LimitsLife = TimeSpan.FromSeconds(10);

    private readonly SettingLayers _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<ConnectionCacheStore> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _held = new(StringComparer.Ordinal);
    private (ConnectionCacheOptions Limits, DateTimeOffset Read)? _limits;
    private long? _bytes;

    public ConnectionCacheStore(
        string folder,
        SettingLayers settings,
        TimeProvider clock,
        ILogger<ConnectionCacheStore> logger
    )
    {
        Folder = Path.GetFullPath(folder);
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The cache's folder (full path).</summary>
    public string Folder { get; }

    /// <summary>Why the cache is off whatever its settings (a folder the app serves); null when it isn't.</summary>
    public string? Disabled { get; private set; }

    /// <summary>The limits of the server's settings (read again every few seconds).</summary>
    public ConnectionCacheOptions Limits
    {
        get
        {
            var now = _clock.GetUtcNow();
            if (_limits is { } known && now - known.Read < LimitsLife)
                return known.Limits;
            var limits = _settings.ServerLevel().Connections.Cache;
            _limits = (limits, now);
            return limits;
        }
    }

    /// <summary>Files may be downloaded and thumbnails kept.</summary>
    public bool IsOn => Disabled is null && Limits.MaxSizeMegabytes > 0;

    /// <summary>The largest file the cache takes, in bytes.</summary>
    public long MaxFileBytes => (long)Math.Max(1, Limits.MaxFileMegabytes) * 1024 * 1024;

    /// <summary>Turns the cache off for the life of the server (<see cref="Disabled"/> says why).</summary>
    public void Disable(string reason)
    {
        Disabled = reason;
        _logger.LogError("The cache of connections is off: {Reason}", reason);
    }

    /// <summary>
    /// The cache's folder is one of <paramref name="served"/> or inside one (the web root, a library's root): files
    /// there could be fetched from the outside.
    /// </summary>
    public bool IsServed(IEnumerable<string> served) =>
        served
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)))
            .Any(folder =>
                Folder.Equals(folder, Paths.LibraryPaths.PathComparison)
                || Folder.StartsWith(folder + Path.DirectorySeparatorChar, Paths.LibraryPaths.PathComparison)
            );

    /// <summary>The path of an entry of <paramref name="connectionId"/>'s cache.</summary>
    public string PathOf(int connectionId, RemoteCacheKey key, string suffix) =>
        Path.Combine(ConnectionFolder(connectionId), key.File[..2], key.Name + suffix);

    /// <summary>A file read from now until <see cref="Release"/>: never evicted meanwhile.</summary>
    public void Hold(string path)
    {
        lock (_gate)
            _held[path] = _held.GetValueOrDefault(path) + 1;
    }

    public void Release(string path)
    {
        lock (_gate)
        {
            if (_held.GetValueOrDefault(path) <= 1)
                _held.Remove(path);
            else
                _held[path]--;
        }
    }

    /// <summary>The file was used now: it goes after those used before it.</summary>
    public void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, _clock.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Gone meanwhile, or read only: it is only evicted sooner.
        }
    }

    /// <summary>Writes a small entry (a thumbnail, what a file is) at once, keeping the limits.</summary>
    public void Write(string path, byte[] bytes)
    {
        MakeRoom(bytes.Length);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}{PartSuffix}";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
        Added(bytes.Length);
    }

    /// <summary>Makes room for <paramref name="incoming"/> bytes: the files used longest ago go.</summary>
    public void MakeRoom(long incoming)
    {
        var limit = (long)Limits.MaxSizeMegabytes * 1024 * 1024;
        lock (_gate)
        {
            _bytes ??= Measure();
            if (_bytes + incoming <= limit)
                return;
            Evict((long)(limit * EvictTo) - incoming, olderThan: null);
        }
    }

    /// <summary>A file of <paramref name="bytes"/> was added.</summary>
    public void Added(long bytes)
    {
        lock (_gate)
            _bytes = (_bytes ?? Measure()) + bytes;
    }

    /// <summary>The other versions of a file than <paramref name="key"/>'s: they are of no use any more.</summary>
    public void ForgetOtherVersions(int connectionId, RemoteCacheKey key)
    {
        var folder = Path.GetDirectoryName(PathOf(connectionId, key, ""))!;
        if (!Directory.Exists(folder))
            return;
        lock (_gate)
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles(key.File + "-*"))
            {
                if (!file.Name.StartsWith(key.Name, StringComparison.Ordinal) && !IsBusy(file))
                    Delete(file);
            }
        }
    }

    /// <summary>Removes a connection's cache (the connection was removed).</summary>
    public void Forget(int connectionId)
    {
        var folder = ConnectionFolder(connectionId);
        if (!Directory.Exists(folder))
            return;
        lock (_gate)
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
                Delete(file);
            TryDeleteFolder(folder);
        }
        _logger.LogInformation("Cache of connection {Connection} removed", connectionId);
    }

    /// <summary>
    /// Removes what nobody used for <see cref="ConnectionCacheOptions.MaxAgeDays"/>, and the caches of connections not
    /// in <paramref name="existing"/> (removed while the server was off, or with their account); null keeps them all.
    /// </summary>
    public void Sweep(IReadOnlySet<int>? existing)
    {
        if (!Directory.Exists(Folder))
            return;
        foreach (var folder in Directory.EnumerateDirectories(Folder))
        {
            var name = Path.GetFileName(folder);
            var known = int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id);
            if (existing is not null && (!known || !existing.Contains(id)))
                Forget(id, folder);
        }
        var oldest = _clock.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, Limits.MaxAgeDays));
        lock (_gate)
        {
            // Measured first: an unknown size would read as over any target and evict everything.
            _bytes ??= Measure();
            Evict(long.MaxValue, oldest);
        }
    }

    private void Forget(int id, string folder)
    {
        if (Path.GetFileName(folder) == id.ToString(CultureInfo.InvariantCulture))
            Forget(id);
    }

    private string ConnectionFolder(int connectionId) =>
        Path.Combine(Folder, connectionId.ToString(CultureInfo.InvariantCulture));

    /// <summary>Deletes, oldest first, down to <paramref name="target"/> bytes and everything before
    /// <paramref name="olderThan"/>. Under the lock.</summary>
    private void Evict(long target, DateTime? olderThan)
    {
        if (!Directory.Exists(Folder))
            return;
        var files = new DirectoryInfo(Folder)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => !IsBusy(file))
            .OrderBy(file => file.LastWriteTimeUtc);
        var evicted = 0;
        foreach (var file in files)
        {
            var old = olderThan is { } limit && file.LastWriteTimeUtc < limit;
            if (_bytes <= target && !old)
                break;
            if (Delete(file))
                evicted++;
        }
        if (evicted > 0)
            _logger.LogDebug("{Count} files evicted from the cache of connections", evicted);
    }

    /// <summary>Read now, or being downloaded: a part left by a stopped server goes after a day.</summary>
    private bool IsBusy(FileInfo file)
    {
        if (file.Name.EndsWith(PartSuffix, StringComparison.Ordinal))
            return file.LastWriteTimeUtc > _clock.GetUtcNow().UtcDateTime.AddDays(-1);
        return _held.ContainsKey(file.FullName);
    }

    private bool Delete(FileInfo file)
    {
        try
        {
            var length = file.Length;
            file.Delete();
            _bytes = Math.Max(0, (_bytes ?? 0) - length);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still read: the next sweep takes it.
        }
    }

    private long Measure() =>
        Directory.Exists(Folder)
            ? new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
            : 0;
}
