using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.KeepAlive;

/// <summary>What keep alive knows of one folder.</summary>
/// <param name="IsNetwork">Null until the first ping looked.</param>
/// <param name="LastPing">When the last ping ended (null: none yet).</param>
/// <param name="Error">Why the last ping failed; null when it worked.</param>
/// <param name="FailingSince">The first failed ping of the current run of failures.</param>
public sealed record KeepAliveFolder(string Path, bool? IsNetwork, DateTimeOffset? LastPing, TimeSpan? Duration, string? Error, DateTimeOffset? FailingSince, bool Waiting);

/// <summary>
/// Keeps the library's folder alive (<see cref="KeepAliveOptions"/>): every interval, one directory query on it, so the
/// file server never sees the session idle. Run by the API's hosted service (<see cref="RunAsync"/>), one per library.
/// </summary>
/// <remarks>
/// Every file system call (finding whether the folder is on the network, the ping) runs on the thread pool under the
/// timeout: a dead share can block a call for minutes, and the loop never waits on it. A folder whose ping is still
/// blocked is not pinged again until it ends, so a dead share holds one thread at most. The log gets a warning when a
/// folder starts failing and a line when it works again, never one per ping.
/// </remarks>
public sealed class KeepAliveService : IDisposable
{
    private readonly LibraryPaths _paths;
    private readonly IOptionsMonitor<SfeOptions> _options;
    private readonly ILogger<KeepAliveService> _logger;
    private readonly ConcurrentDictionary<string, Folder> _folders = new(LibraryPaths.PathComparer);
    private readonly SemaphoreSlim _changed = new(0);
    private readonly IDisposable? _subscription;

    public KeepAliveService(LibraryPaths paths, IOptionsMonitor<SfeOptions> options, ILogger<KeepAliveService> logger)
    {
        _paths = paths;
        _options = options;
        _logger = logger;
        _subscription = options.OnChange(_ =>
        {
            // A mapped drive may have appeared, a mode changed: look again, and ping now rather than after the old interval.
            foreach (var folder in _folders.Values)
                folder.IsNetwork = null;
            if (_changed.CurrentCount == 0)
                _changed.Release();
        });
    }

    /// <summary>The ping: one directory query, whatever the folder holds. Replaced by tests.</summary>
    internal Func<string, Task> Ping { get; set; } = path => Task.Run(() => Directory.EnumerateFileSystemEntries(path).FirstOrDefault());

    /// <summary>Replaced by tests.</summary>
    internal Func<string, bool> IsNetwork { get; set; } = NetworkFolders.IsNetwork;

    private KeepAliveOptions Settings => _options.CurrentValue.KeepAlive;

    public IReadOnlyList<KeepAliveFolder> Status() =>
        _folders.Values.Select(f => f.ToView()).OrderBy(f => f.Path, LibraryPaths.PathComparer).ToList();

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
    public async Task PingAllAsync(CancellationToken cancellationToken = default)
    {
        var settings = Settings;
        if (settings.Mode == KeepAliveMode.Off)
            return;

        var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, KeepAliveOptions.MinTimeoutSeconds, KeepAliveOptions.MaxTimeoutSeconds));
        var rounds = Targets()
            .Select(path => _folders.GetOrAdd(path, p => new Folder(p)))
            .Select(folder => PingAsync(folder, settings.Mode, timeout, cancellationToken))
            .ToList();
        await Task.WhenAll(rounds);
    }

    /// <summary>The folders kept alive: the library's root (more folders come in a later version).</summary>
    private IEnumerable<string> Targets() => [_paths.Root];

    private async Task PingAsync(Folder folder, KeepAliveMode mode, TimeSpan timeout, CancellationToken cancellationToken)
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
            await Ping(folder.Path);
            return true;
        }, cancellationToken);
        folder.Running = running;
        // A ping given up on may still fail later: observe it, nobody else will.
        _ = running.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        try
        {
            if (await running.WaitAsync(timeout, cancellationToken))
                Succeeded(folder, watch.Elapsed);
            else
                Skipped(folder);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            Failed(folder, $"no answer within {timeout.TotalSeconds:0} s");
        }
        catch (Exception ex)
        {
            Failed(folder, ex.Message);
        }
    }

    private void Succeeded(Folder folder, TimeSpan duration)
    {
        if (folder.FailingSince is { } since)
            _logger.LogInformation("Keep alive: {Folder} answers again (failing since {Since:u})", folder.Path, since);
        else if (folder.LastPing is null)
            _logger.LogInformation("Keep alive: pinging {Folder} every {Interval} s", folder.Path, Settings.IntervalSeconds);
        folder.Record(duration, null);
    }

    private void Failed(Folder folder, string error)
    {
        if (folder.FailingSince is null)
            _logger.LogWarning("Keep alive: {Folder} does not answer: {Error}", folder.Path, error);
        folder.Record(null, error);
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
    }

    private sealed class Folder(string path)
    {
        private readonly Lock _gate = new();
        private DateTimeOffset? _lastPing;
        private TimeSpan? _duration;
        private string? _error;
        private DateTimeOffset? _failingSince;

        public string Path { get; } = path;
        public volatile Task<bool>? Running;
        public bool? IsNetwork;
        public bool ToldNotNetwork;

        public DateTimeOffset? LastPing { get { lock (_gate) return _lastPing; } }
        public DateTimeOffset? FailingSince { get { lock (_gate) return _failingSince; } }

        public void Record(TimeSpan? duration, string? error)
        {
            lock (_gate)
            {
                _lastPing = DateTimeOffset.UtcNow;
                _duration = duration;
                _error = error;
                _failingSince = error is null ? null : _failingSince ?? _lastPing;
            }
        }

        public KeepAliveFolder ToView()
        {
            lock (_gate)
                return new KeepAliveFolder(Path, IsNetwork, _lastPing, _duration, _error, _failingSince, Running is { IsCompleted: false });
        }
    }
}
