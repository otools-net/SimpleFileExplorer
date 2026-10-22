using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Files;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.KeepAlive;

/// <summary>What keep alive knows of one folder.</summary>
/// <param name="IsRoot">The library's folder itself (the others come from <see cref="KeepAliveOptions.Folders"/>).</param>
/// <param name="IsNetwork">Null until the first ping looked.</param>
/// <param name="LastPing">When the last ping ended (null: none yet).</param>
/// <param name="Error">Why the last ping failed; null when it worked.</param>
/// <param name="FailingSince">The first failed ping of the current run of failures.</param>
/// <param name="Method">How the last ping was made; null: none yet.</param>
public sealed record KeepAliveFolder(
    string Path, bool IsRoot, bool? IsNetwork, DateTimeOffset? LastPing, TimeSpan? Duration, string? Error, DateTimeOffset? FailingSince, bool Waiting,
    KeepAliveMethod? Method);

/// <summary>How keep alive goes, as a whole: what the indicator of the top bar shows.</summary>
public enum KeepAliveState
{
    /// <summary>Off, or nothing pinged yet (with <see cref="KeepAliveMode.Auto"/>: no folder on the network).</summary>
    None,

    /// <summary>Every folder pinged answered its last ping.</summary>
    Ok,

    /// <summary>A folder did not answer its last ping.</summary>
    Failing,
}

/// <summary>
/// Keeps the library's folder alive (<see cref="KeepAliveOptions"/>): every interval, one directory query on it and on
/// the more folders set, or a small file written and deleted in the work folder (<see cref="KeepAliveMethod.Touch"/>),
/// so the file server never sees the session idle. Run by the API's hosted service (<see cref="RunAsync"/>), one per
/// library.
/// </summary>
/// <remarks>
/// Every file system call (finding whether the folder is on the network, the ping) runs on the thread pool under the
/// timeout: a dead share can block a call for minutes, and the loop never waits on it. A folder whose ping is still
/// blocked is not pinged again until it ends, so a dead share holds one thread at most. The log gets a warning when a
/// folder starts failing and a line when it works again, never one per ping.
/// </remarks>
public sealed class KeepAliveService : IDisposable
{
    /// <summary>The sub-folder of the work folder the write ping uses.</summary>
    public const string WorkPart = "keep-alive";

    private readonly LibraryPaths _paths;
    private readonly WorkFolder _workFolder;
    private readonly IOptionsMonitor<SfeOptions> _options;
    private readonly ILogger<KeepAliveService> _logger;
    private readonly ConcurrentDictionary<string, Folder> _folders = new(LibraryPaths.PathComparer);
    private readonly SemaphoreSlim _changed = new(0);
    private readonly SemaphoreSlim _round = new(1, 1);
    private readonly IDisposable? _subscription;

    /// <summary>What was already said since the settings last changed (refused folders, the write ping read instead).</summary>
    private readonly ConcurrentDictionary<string, bool> _told = new(StringComparer.Ordinal);

    public KeepAliveService(LibraryPaths paths, WorkFolder workFolder, IOptionsMonitor<SfeOptions> options, ILogger<KeepAliveService> logger)
    {
        _paths = paths;
        _workFolder = workFolder;
        _options = options;
        _logger = logger;
        _subscription = options.OnChange(_ =>
        {
            // A mapped drive may have appeared, a mode changed: look again, and ping now rather than after the old interval.
            foreach (var folder in _folders.Values)
                folder.IsNetwork = null;
            _told.Clear();
            // Turned off: the indicator goes at once, not at the next round.
            UpdateState();
            if (_changed.CurrentCount == 0)
                _changed.Release();
        });
        Touch = () => Task.Run(() =>
        {
            var file = Path.Combine(_workFolder.Get(WorkPart), "ping.tmp");
            File.WriteAllText(file, DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(file);
        });
    }

    /// <summary>The ping: one directory query, whatever the folder holds. Replaced by tests.</summary>
    internal Func<string, Task> Ping { get; set; } = path => Task.Run(() => Directory.EnumerateFileSystemEntries(path).FirstOrDefault());

    /// <summary>The write ping: a small file created and deleted in the work folder. Replaced by tests.</summary>
    internal Func<Task> Touch { get; set; }

    /// <summary>Replaced by tests.</summary>
    internal Func<string, bool> IsNetwork { get; set; } = NetworkFolders.IsNetwork;

    private KeepAliveOptions Settings => _options.CurrentValue.KeepAlive;

    /// <summary>
    /// The root as <see cref="LibraryPaths.TryResolve"/> gives folders (no ending separator, except a drive's root), so
    /// that a folder of the settings naming the root is the root.
    /// </summary>
    private string RootPath => Path.TrimEndingDirectorySeparator(_paths.Root);

    /// <summary>Raised when <see cref="State"/> changes (after a round, or when the settings turn it off), on that thread.</summary>
    public event Action<KeepAliveState>? StateChanged;

    private readonly Lock _stateGate = new();
    private volatile KeepAliveState _state;

    /// <summary>How keep alive goes as a whole; only folders pinged count (not those <see cref="KeepAliveMode.Auto"/> leaves alone).</summary>
    public KeepAliveState State => _state;

    public IReadOnlyList<KeepAliveFolder> Status() =>
        _folders.Values.Select(f => f.ToView()).OrderBy(f => !f.IsRoot).ThenBy(f => f.Path, LibraryPaths.PathComparer).ToList();

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PingAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Keep alive failed");
            }

            var interval = TimeSpan.FromSeconds(Math.Clamp(Settings.IntervalSeconds, KeepAliveOptions.MinIntervalSeconds, KeepAliveOptions.MaxIntervalSeconds));
            try
            {
                // Off waits too: a change of the settings wakes it up.
                await _changed.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One round: pings every folder kept alive, each under the timeout.</summary>
    public Task PingAllAsync(CancellationToken cancellationToken = default) => RoundAsync(Settings.Mode, cancellationToken);

    /// <summary>
    /// A round asked for by hand (Ping now): also while keep alive is off, as <see cref="KeepAliveMode.Always"/> would,
    /// to try a share before turning it on.
    /// </summary>
    public async Task<IReadOnlyList<KeepAliveFolder>> PingNowAsync(CancellationToken cancellationToken = default)
    {
        var mode = Settings.Mode;
        await RoundAsync(mode == KeepAliveMode.Off ? KeepAliveMode.Always : mode, cancellationToken);
        return Status();
    }

    private async Task RoundAsync(KeepAliveMode mode, CancellationToken cancellationToken)
    {
        if (mode == KeepAliveMode.Off)
            return;

        // One round at a time (the loop and Ping now): a round lasts the timeout at most.
        await _round.WaitAsync(cancellationToken);
        try
        {
            var settings = Settings;
            var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, KeepAliveOptions.MinTimeoutSeconds, KeepAliveOptions.MaxTimeoutSeconds));
            var targets = Targets(settings);
            // Folders taken off the settings leave the status.
            foreach (var gone in _folders.Keys.Where(k => !targets.Contains(k)).ToList())
                _folders.TryRemove(gone, out _);
            var pings = targets
                .Select(path => _folders.GetOrAdd(path, p => new Folder(p, LibraryPaths.PathComparer.Equals(p, RootPath))))
                .Select(folder => PingAsync(folder, mode, MethodFor(folder, settings), timeout, cancellationToken))
                .ToList();
            await Task.WhenAll(pings);
        }
        finally
        {
            _round.Release();
            UpdateState();
        }
    }

    private void UpdateState()
    {
        var pinged = Settings.Mode == KeepAliveMode.Off ? [] : _folders.Values.Select(f => f.ToView()).Where(f => f.LastPing is not null).ToList();
        var state = pinged.Count == 0 ? KeepAliveState.None
            : pinged.Any(f => f.Error is not null) ? KeepAliveState.Failing
            : KeepAliveState.Ok;
        lock (_stateGate)
        {
            if (state == _state)
                return;
            _state = state;
        }
        StateChanged?.Invoke(state);
    }

    /// <summary>The library's root, then the more folders set that resolve inside the library (each once).</summary>
    private HashSet<string> Targets(KeepAliveOptions settings)
    {
        var targets = new HashSet<string>(LibraryPaths.PathComparer) { RootPath };
        foreach (var folder in settings.Folders)
        {
            if (_paths.TryResolve(folder, out var full))
                targets.Add(full);
            else if (_told.TryAdd("refused:" + folder, true))
                _logger.LogWarning("Keep alive: “{Folder}” is not a folder of the library, left out", folder);
        }

        return targets;
    }

    /// <summary>The write ping only for the root, and only when the work folder is in the library (on its share).</summary>
    private KeepAliveMethod MethodFor(Folder folder, KeepAliveOptions settings)
    {
        if (!folder.IsRoot || settings.Method != KeepAliveMethod.Touch)
            return KeepAliveMethod.List;
        if (_workFolder.Location == WorkFolderLocation.Library)
            return KeepAliveMethod.Touch;
        if (_told.TryAdd("touch-on-server", true))
            _logger.LogWarning("Keep alive: the work folder is on the server, not in the library: {Folder} is read instead of written", folder.Path);
        return KeepAliveMethod.List;
    }

    private async Task PingAsync(Folder folder, KeepAliveMode mode, KeepAliveMethod method, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (folder.Running is { IsCompleted: false })
        {
            // Still blocked since an earlier round: it stays failed, already logged.
            _logger.LogDebug("Keep alive: {Folder} still has not answered the previous ping", folder.Path);
            return;
        }

        var watch = Stopwatch.StartNew();
        var running = Task.Run(async () =>
        {
            folder.IsNetwork ??= IsNetwork(folder.Path);
            if (mode == KeepAliveMode.Auto && folder.IsNetwork == false)
                return false;
            await (method == KeepAliveMethod.Touch ? Touch() : Ping(folder.Path));
            return true;
        }, cancellationToken);
        folder.Running = running;
        // A ping given up on may still fail later: observe it, nobody else will.
        _ = running.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        try
        {
            if (await running.WaitAsync(timeout, cancellationToken))
                Succeeded(folder, method, watch.Elapsed);
            else
                Skipped(folder);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            Failed(folder, method, $"no answer within {timeout.TotalSeconds:0} s");
        }
        catch (Exception ex)
        {
            Failed(folder, method, ex.Message);
        }
    }

    private void Succeeded(Folder folder, KeepAliveMethod method, TimeSpan duration)
    {
        if (folder.FailingSince is { } since)
            _logger.LogInformation("Keep alive: {Folder} answers again (failing since {Since:u})", folder.Path, since);
        else if (folder.LastPing is null)
            _logger.LogInformation("Keep alive: pinging {Folder} every {Interval} s", folder.Path, Settings.IntervalSeconds);
        folder.Record(method, duration, null);
    }

    private void Failed(Folder folder, KeepAliveMethod method, string error)
    {
        if (folder.FailingSince is null)
            _logger.LogWarning("Keep alive: {Folder} does not answer: {Error}", folder.Path, error);
        folder.Record(method, null, error);
    }

    private void Skipped(Folder folder)
    {
        if (!folder.ToldNotNetwork)
            _logger.LogInformation("Keep alive: {Folder} is not on the network, not pinged (mode Always pings it anyway)", folder.Path);
        folder.ToldNotNetwork = true;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _changed.Dispose();
        _round.Dispose();
    }

    private sealed class Folder(string path, bool isRoot)
    {
        private readonly Lock _gate = new();
        private DateTimeOffset? _lastPing;
        private TimeSpan? _duration;
        private string? _error;
        private DateTimeOffset? _failingSince;
        private KeepAliveMethod? _method;

        public string Path { get; } = path;
        public bool IsRoot { get; } = isRoot;
        public volatile Task<bool>? Running;
        public bool? IsNetwork;
        public bool ToldNotNetwork;

        public DateTimeOffset? LastPing { get { lock (_gate) return _lastPing; } }
        public DateTimeOffset? FailingSince { get { lock (_gate) return _failingSince; } }

        public void Record(KeepAliveMethod method, TimeSpan? duration, string? error)
        {
            lock (_gate)
            {
                _lastPing = DateTimeOffset.UtcNow;
                _method = method;
                _duration = duration;
                _error = error;
                _failingSince = error is null ? null : _failingSince ?? _lastPing;
            }
        }

        public KeepAliveFolder ToView()
        {
            lock (_gate)
                return new KeepAliveFolder(Path, IsRoot, IsNetwork, _lastPing, _duration, _error, _failingSince, Running is { IsCompleted: false }, _method);
        }
    }
}
