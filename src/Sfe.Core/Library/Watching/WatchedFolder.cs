using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.KeepAlive;

namespace Otools.Net.Sfe.Core.Library.Watching;

/// <summary>
/// One folder followed for <see cref="FolderWatchers"/>: by the system's watcher (its own items only, not below), or
/// read again every <see cref="FolderWatchTimings.PollEvery"/> at least (a network mount outside Windows, whose
/// watcher sees only this computer's changes; a watcher refused or failing). Changes settle before they are told, and
/// a folder is told at most every <see cref="FolderWatchTimings.MinGap"/>.
/// </summary>
internal sealed class WatchedFolder(FolderWatchers owner, string folder, string fullPath) : IDisposable
{
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _settle;
    private Timer? _poll;
    private FolderSignature? _signature;
    private bool _rebase;
    private long _pendingSince = -1;
    private long _lastTold;
    private bool _disposed;

    /// <summary>When SFE last changed the folder (<see cref="Environment.TickCount64"/>; 0: never).</summary>
    public long LastOwnChange;

    /// <summary>The holds on it (<see cref="FolderWatch"/>), counted under the owner's lock.</summary>
    public int Leases { get; set; }

    public string Folder => folder;

    public string FullPath => fullPath;

    public bool Polled { get; private set; }

    private FolderWatchTimings Timings => owner.Timings;

    private bool IsTop => folder.Length == 0;

    /// <summary>Starts following it, off the caller's thread (finding a network mount reads the file system).</summary>
    public void Start() => _ = Task.Run(Begin);

    /// <summary>SFE changed the folder: what the watcher (or the next poll) sees of it now is SFE's own.</summary>
    public void OwnChange()
    {
        Interlocked.Exchange(ref LastOwnChange, Math.Max(1, Environment.TickCount64));
        lock (_gate)
        {
            _rebase = Polled;
        }
    }

    private void Begin()
    {
        if (owner.PollOnly || (!OperatingSystem.IsWindows() && NetworkFolders.IsNetwork(fullPath)))
        {
            StartPolling("it is on a network mount");
            return;
        }
        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _watcher = CreateWatcher();
                _watcher.EnableRaisingEvents = true;
            }
            owner.Logger.LogDebug("Folder {Folder}: watched for changes made outside SFE", folder);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException
            )
        {
            // Linux refuses past its limit of watches (fs.inotify.max_user_watches), Windows a folder gone.
            StartPolling(ex.Message);
        }
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter =
                NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Created += (_, e) => Seen(e.Name);
        watcher.Deleted += (_, e) => Seen(e.Name);
        watcher.Changed += (_, e) => Seen(e.Name);
        watcher.Renamed += (_, e) =>
        {
            Seen(e.OldName);
            Seen(e.Name);
        };
        watcher.Error += (_, e) => Failed(e.GetException());
        return watcher;
    }

    private void Seen(string? name)
    {
        if (name is null || !FolderWatchers.IsIgnored(name, IsTop))
            Pending();
    }

    /// <summary>
    /// The watcher lost track: more changes than it could hold (told: the listing reads it all), or broke.
    /// </summary>
    private void Failed(Exception error)
    {
        if (error is not InternalBufferOverflowException)
            StartPolling(error.Message);
        Pending();
    }

    /// <summary>A change: told once changes settle, and not sooner than the gap after the last notice.</summary>
    private void Pending()
    {
        lock (_gate)
        {
            if (_disposed || _pendingSince >= 0)
                return;
            var now = Environment.TickCount64;
            _pendingSince = now;
            var gapLeft = _lastTold == 0 ? 0 : _lastTold + (long)Timings.MinGap.TotalMilliseconds - now;
            var wait = Math.Max((long)Timings.Settle.TotalMilliseconds, gapLeft);
            (_settle ??= new Timer(_ => Flush())).Change(wait, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        long since;
        lock (_gate)
        {
            if (_disposed || _pendingSince < 0)
                return;
            since = _pendingSince;
            _pendingSince = -1;
            _lastTold = Math.Max(1, Environment.TickCount64);
        }
        owner.Tell(this, since);
    }

    private void StartPolling(string why)
    {
        lock (_gate)
        {
            if (_disposed || Polled)
                return;
            Polled = true;
            _watcher?.Dispose();
            _watcher = null;
            _poll = new Timer(_ => Poll());
            _poll.Change(0, Timeout.Infinite);
        }
        owner.Logger.LogDebug(
            "Folder {Folder}: read again every {Seconds:N0} s for changes made outside SFE ({Why})",
            folder,
            Timings.PollEvery.TotalSeconds,
            why
        );
    }

    /// <summary>
    /// Reads the folder again; a change from the last read is told. Never more than a tenth of the time.
    /// </summary>
    private void Poll()
    {
        var started = Stopwatch.GetTimestamp();
        var signature = FolderSignature.Read(fullPath, IsTop);
        var took = Stopwatch.GetElapsedTime(started);
        bool changed;
        lock (_gate)
        {
            if (_disposed)
                return;
            changed = _signature is not null && signature != _signature && !_rebase;
            (_signature, _rebase) = (signature, false);
            var next = TimeSpan.FromTicks(Math.Max(Timings.PollEvery.Ticks, took.Ticks * 10));
            _poll!.Change(next, Timeout.InfiniteTimeSpan);
        }
        if (changed)
            Pending();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _settle?.Dispose();
            _poll?.Dispose();
            (_watcher, _settle, _poll) = (null, null, null);
        }
    }
}
