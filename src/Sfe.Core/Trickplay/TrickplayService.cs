using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Tasks;
using Sfe.Core.Thumbnails;

namespace Sfe.Core.Trickplay;

public enum TrickplayState
{
    /// <summary>Turned off in the settings.</summary>
    Disabled,

    /// <summary>Turned on, but ffmpeg can't be found.</summary>
    Unavailable,
    Idle,
    Running,
}

/// <param name="Checked">Videos looked at by the current (or last) pass over the folders.</param>
/// <param name="Generated">Videos given trickplay by that pass and by the player's requests since it started.</param>
/// <param name="Failed">Videos ffmpeg couldn't handle; tried again when they or the settings change.</param>
/// <param name="Queued">Videos opened in the player, waiting for their trickplay.</param>
/// <param name="Pending">Videos the current pass found without (current) trickplay, waiting for their turn.</param>
/// <param name="Discovering">The current pass is still looking for videos: <see cref="Pending"/> may grow.</param>
/// <param name="Current">Videos being processed.</param>
/// <param name="LastPassFinished">When the last complete pass over the folders ended.</param>
/// <param name="PassStarted">When the pass running started; null between passes.</param>
/// <param name="NextPass">When the next periodic pass starts (<see cref="TrickplayOptions.RecheckHours"/>); null when none is planned.</param>
public sealed record TrickplayStatus(
    TrickplayState State,
    int Checked,
    int Generated,
    int Failed,
    int Queued,
    int Pending,
    bool Discovering,
    IReadOnlyList<string> Current,
    DateTimeOffset? LastPassFinished,
    DateTimeOffset? PassStarted = null,
    DateTimeOffset? NextPass = null);

/// <summary>Where a video stands in the background job; videos with nothing to do have no state.</summary>
public enum TrickplayFileState
{
    /// <summary>Waiting for its turn (opened in the player, or found by a pass).</summary>
    Pending,

    /// <summary>ffmpeg is working on it.</summary>
    Running,
}

/// <summary>A video being processed, since <paramref name="Started"/>.</summary>
public sealed record TrickplayRunning(string Path, DateTimeOffset Started);

/// <summary>A video the job finished with.</summary>
/// <param name="Error">Why there is no trickplay, when <paramref name="Succeeded"/> is false.</param>
/// <param name="Seconds">Time ffmpeg took.</param>
public sealed record TrickplayResult(string Path, bool Succeeded, string? Error, DateTimeOffset Finished, double Seconds);

/// <summary>Detailed progress of the background job, for the activity dialog.</summary>
/// <param name="Queued">The first videos opened in the player and waiting (they go first); the total is in the status.</param>
/// <param name="Pending">The first videos the pass will process next; the total is in the status.</param>
/// <param name="Recent">The last videos finished, the most recent first.</param>
public sealed record TrickplayActivity(
    TrickplayStatus Status,
    IReadOnlyList<TrickplayRunning> Running,
    IReadOnlyList<string> Queued,
    IReadOnlyList<string> Pending,
    IReadOnlyList<TrickplayResult> Recent);

/// <summary>Trickplay of a video: <see cref="Count"/> frames, one every <see cref="Interval"/> seconds, tiled on sheets.</summary>
/// <param name="Interval">Seconds between two frames; frame <c>n</c> shows second <c>n × Interval</c>.</param>
/// <param name="Columns">Frames per row of a sheet, left to right then top to bottom.</param>
/// <param name="TileWidth">Size of one frame in pixels.</param>
/// <param name="Sheets">Sheet images, numbered from 0. The last one can be partly empty.</param>
/// <param name="Version">Changes when the trickplay is generated again: sheet URLs carry it, so browsers may cache them.</param>
public sealed record TrickplayManifest(int Interval, int Count, int Columns, int Rows, int TileWidth, int TileHeight, int Sheets, long Version);

/// <summary>
/// Generates trickplay in the background for the videos of the configured folders, and serves it.
/// </summary>
/// <remarks>
/// <para>
/// Layout: <c>{CacheDirectory}/trickplay/{k[0..2]}/{k[2..4]}/{k[4..]}/</c> holds <c>info.json</c> and the sheets
/// <c>0.jpg</c>, <c>1.jpg</c>… (same keys as the thumbnails, <see cref="ThumbnailService.CacheKey(string)"/>). <c>info.json</c>
/// records the size and date of the video and the settings used, so a changed video or new settings make it stale.
/// A failure is recorded too, so that one broken file isn't sent to ffmpeg again on every pass.
/// </para>
/// <para>
/// Two loops run in <see cref="RunAsync"/>: passes over the folders (at start-up, when the settings change, every
/// <see cref="TrickplayOptions.RecheckHours"/> or on request), and the queue of videos opened in the player, which
/// don't wait for a pass to reach them. Each pass only reads <c>info.json</c> for videos that are done, so it is
/// cheap after the first one.
/// </para>
/// <para>
/// A pass first lists the videos to do (while the work on the first ones already runs), so that the waiting videos
/// can be shown: per file in the listings (<see cref="GetStates"/>) and as a list (<see cref="GetActivity"/>).
/// </para>
/// <para>
/// Each trickplay also gets <c>fingerprint.bin</c> (<see cref="VideoFingerprint"/>), read back from its sheets.
/// </para>
/// </remarks>
public sealed class TrickplayService : IDisposable
{
    public const string FolderName = "trickplay";

    /// <summary>Paths one <see cref="GetStates"/> call may name.</summary>
    public const int MaxStatePaths = 5000;

    private const string InfoFile = "info.json";
    private const string FingerprintFile = "fingerprint.bin";
    private const int RecentCount = 50;
    private const int MaxErrorLength = 300;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly LibraryPaths _paths;
    private readonly MediaClassifier _classifier;
    private readonly LibraryBrowser _browser;
    private readonly Ffmpeg _ffmpeg;
    private readonly IOptionsMonitor<SfeOptions> _options;
    private readonly ILogger<TrickplayService> _logger;
    private readonly TaskHistory _history;
    private readonly IDisposable? _subscription;

    private readonly SemaphoreSlim _passSignal = new(0);
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly PendingVideos _requested = new();      // opened in the player
    private readonly PendingVideos _pending = new();        // found by the current pass
    private readonly ConcurrentDictionary<string, DateTimeOffset> _running = new(LibraryPaths.PathComparer);
    private readonly Queue<TrickplayResult> _recent = new();
    private volatile bool _passRequested = true;
    private volatile bool _cancelRequested;
    private TaskTrigger _passTrigger = TaskTrigger.Automatic;
    private int? _passScheduleId;
    private DateTimeOffset? _passStarted;
    private DateTimeOffset? _nextPass;

    /// <summary>Cancelled (and replaced) by <see cref="Cancel"/>: stops the video of the player's queue being made.</summary>
    private CancellationTokenSource _queueWork = new();
    private volatile bool _discovering;
    private volatile CancellationTokenSource? _pass;
    private string _passSettings;
    private readonly FfmpegLimits _limits;
    private int _checked;
    private int _generated;
    private int _failed;
    private DateTimeOffset? _lastPassFinished;

    public TrickplayService(
        LibraryPaths paths,
        MediaClassifier classifier,
        LibraryBrowser browser,
        Ffmpeg ffmpeg,
        IOptionsMonitor<SfeOptions> options,
        TaskHistory history,
        FfmpegLimits limits,
        ILogger<TrickplayService> logger)
    {
        _history = history;
        _limits = limits;
        _paths = paths;
        _classifier = classifier;
        _browser = browser;
        _ffmpeg = ffmpeg;
        _options = options;
        _logger = logger;
        _passSettings = PassSettings(options.CurrentValue);
        _subscription = options.OnChange(OnOptionsChanged);
    }

    private TrickplayOptions Options => _options.CurrentValue.Trickplay;

    /// <inheritdoc cref="TrickplayOptions.RecheckHours"/>
    public int RecheckHours => Options.RecheckHours;

    /// <summary>Enabled and ffmpeg found.</summary>
    public bool IsAvailable => Options.Enabled && _ffmpeg.IsAvailable;

    public TrickplayStatus Status => new(
        !Options.Enabled ? TrickplayState.Disabled
            : !_ffmpeg.IsAvailable ? TrickplayState.Unavailable
            : _pass is not null || !_running.IsEmpty || _requested.Count > 0 ? TrickplayState.Running
            : TrickplayState.Idle,
        _checked,
        _generated,
        _failed,
        _requested.Count,
        _pending.Count,
        _discovering,
        [.. _running.Keys.Select(Relative).Order(StringComparer.Ordinal)],
        _lastPassFinished,
        _pass is not null ? _passStarted : null,
        IsAvailable ? _nextPass : null);

    /// <summary>Status with the videos being processed and the first <paramref name="limit"/> waiting ones.</summary>
    public TrickplayActivity GetActivity(int limit)
    {
        List<TrickplayResult> recent;
        lock (_recent)
            recent = [.. _recent.Reverse()];
        return new TrickplayActivity(
            Status,
            [.. _running.Select(r => new TrickplayRunning(Relative(r.Key), r.Value)).OrderBy(r => r.Started)],
            [.. _requested.First(limit).Select(Relative)],
            [.. _pending.First(limit).Select(Relative)],
            recent);
    }

    /// <summary>
    /// States of the videos at <paramref name="relativePaths"/> (by the given path) that are waiting or being
    /// processed; the others are left out. Cost proportional to the paths named, not to the queue.
    /// </summary>
    public Dictionary<string, TrickplayFileState> GetStates(IEnumerable<string> relativePaths)
    {
        var states = new Dictionary<string, TrickplayFileState>(StringComparer.Ordinal);
        if (_running.IsEmpty && _requested.Count == 0 && _pending.Count == 0)
            return states;

        foreach (var path in relativePaths)
        {
            if (!_paths.TryResolve(path, out var full))
                continue;
            if (_running.ContainsKey(full))
                states[path] = TrickplayFileState.Running;
            else if (_requested.Contains(full) || _pending.Contains(full))
                states[path] = TrickplayFileState.Pending;
        }

        return states;
    }

    private string Relative(string fullPath) => _paths.ToRelative(fullPath) ?? fullPath;

    /// <summary>
    /// Stops the pass running and the video of the player's queue being made, and forgets the videos waiting; a
    /// pass asked for but not started yet is dropped too. The periodic passes go on as planned.
    /// </summary>
    /// <returns>false when there was nothing to stop.</returns>
    public bool Cancel()
    {
        var cancelled = _requested.Count > 0 || !_running.IsEmpty;
        _passRequested = false;
        if (_pass is { } pass)
        {
            _cancelRequested = true;
            try
            {
                pass.Cancel();
                cancelled = true;
            }
            catch (ObjectDisposedException)
            {
                // The pass just ended.
            }
        }

        _requested.Clear();
        Interlocked.Exchange(ref _queueWork, new CancellationTokenSource()).Cancel();
        if (cancelled)
            _logger.LogInformation("Trickplay cancelled");
        return cancelled;
    }

    /// <summary>Starts a pass over the folders now (after the current one, if one is running).</summary>
    /// <param name="scheduleId">Schedule asking for it, for the run history.</param>
    /// <returns>false when trickplay is disabled or ffmpeg is missing.</returns>
    public bool RequestPass(TaskTrigger trigger = TaskTrigger.Manual, int? scheduleId = null)
    {
        if (!IsAvailable)
            return false;
        _passTrigger = trigger;
        _passScheduleId = scheduleId;
        _passRequested = true;
        _passSignal.Release();
        return true;
    }

    /// <summary>
    /// Trickplay of a video, or null when there is none yet. A video of the configured folders without (current)
    /// trickplay is queued, ahead of the background pass: someone is watching it. Not with <paramref name="queue"/>
    /// false: a glance (hovering its thumbnail) only shows what is there.
    /// </summary>
    public TrickplayManifest? Get(string? relativePath, bool queue = true)
    {
        if (!Options.Enabled || !_paths.TryResolve(relativePath, out var source))
            return null;

        var video = new FileInfo(source);
        if (!video.Exists || _classifier.Classify(video.Extension) != ItemKind.Video)
            return null;

        var settings = Options;
        var info = ReadInfo(GetCacheFolder(source));
        if (IsFresh(info, video, settings))
            return info!.Manifest;

        if (queue && IsAvailable && IsInFolders(source, settings))
            Enqueue(source);

        // Made with other settings, but of this very file: its frames are still right until the new ones are ready.
        return info is { Manifest: not null } && SameSource(info, video) ? info.Manifest : null;
    }

    /// <returns>Path of sheet <paramref name="index"/> of a video, or null when there is no such sheet.</returns>
    public string? GetSheet(string? relativePath, int index)
    {
        if (index < 0 || !_paths.TryResolve(relativePath, out var source))
            return null;

        var sheet = Path.Combine(GetCacheFolder(source), $"{index}.jpg");
        return File.Exists(sheet) ? sheet : null;
    }

    public string GetCacheFolder(string sourceFullPath) =>
        Path.Combine(_options.CurrentValue.Thumbnails.CacheDirectory, FolderName, ThumbnailService.ShardedPath(ThumbnailService.CacheKey(sourceFullPath)));

    /// <summary>Background work, until <paramref name="stoppingToken"/> is cancelled (application shutdown).</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(PassLoopAsync(stoppingToken), QueueLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void Enqueue(string source)
    {
        if (_running.ContainsKey(source))
            return;
        // Waiting for the pass to reach it: moves ahead, someone is watching it.
        _pending.Remove(source);
        if (_requested.Add(source))
            _queueSignal.Release();
    }

    private async Task PassLoopAsync(CancellationToken stoppingToken)
    {
        var nextPass = DateTimeOffset.MaxValue;
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();
            if (!IsAvailable)
            {
                // Woken up by a settings change.
                await _passSignal.WaitAsync(stoppingToken);
                continue;
            }

            if (_passRequested || DateTimeOffset.UtcNow >= nextPass)
            {
                var (trigger, scheduleId) = _passRequested ? (_passTrigger, _passScheduleId) : (TaskTrigger.Automatic, null);
                _passRequested = false;
                _passTrigger = TaskTrigger.Automatic;
                _passScheduleId = null;
                _nextPass = null;
                await PassAsync(trigger, scheduleId, stoppingToken);
                var hours = Options.RecheckHours;
                nextPass = hours > 0 ? DateTimeOffset.UtcNow.AddHours(hours) : DateTimeOffset.MaxValue;
                _nextPass = hours > 0 ? nextPass : null;
                continue;
            }

            var wait = nextPass == DateTimeOffset.MaxValue ? Timeout.InfiniteTimeSpan : nextPass - DateTimeOffset.UtcNow;
            if (wait == Timeout.InfiniteTimeSpan || wait > TimeSpan.Zero)
                await _passSignal.WaitAsync(wait > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : wait, stoppingToken);
        }
    }

    private async Task PassAsync(TaskTrigger trigger, int? scheduleId, CancellationToken stoppingToken)
    {
        using var pass = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var found = new SemaphoreSlim(0);
        var settings = Options;
        var workers = Math.Max(1, settings.Parallelism);
        _checked = 0;
        _generated = 0;
        _failed = 0;
        _discovering = true;
        _cancelRequested = false;
        _passStarted = DateTimeOffset.Now;
        _pass = pass;
        // From here on, what the pass logs goes into the run's log.
        var run = _history.Begin(BackgroundTaskKind.Trickplay, null, trigger, scheduleId);
        var outcome = TaskOutcome.Interrupted;
        string? note = null;
        _logger.LogInformation("Trickplay pass started");
        try
        {
            // One task lists the videos to do (cheap: only their info.json is read), the workers take them in order.
            var discovery = Task.Run(() => Discover(settings, found, workers, pass.Token), pass.Token);
            await Task.WhenAll([discovery, .. Enumerable.Range(0, workers).Select(_ => WorkAsync(settings, found, pass.Token))]);
            _lastPassFinished = DateTimeOffset.Now;
            _logger.LogInformation("Trickplay pass finished: {Checked} videos checked, {Generated} generated, {Failed} failed", _checked, _generated, _failed);
            outcome = TaskOutcome.Completed;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested && _cancelRequested)
        {
            _logger.LogInformation("Trickplay pass cancelled");
            outcome = TaskOutcome.Cancelled;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The settings changed: the loop starts a new pass with them (see OnOptionsChanged).
            _logger.LogInformation("Trickplay pass interrupted by a settings change");
            outcome = TaskOutcome.Cancelled;
            note = "Interrupted by a settings change, a new pass follows";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Trickplay pass failed");
            outcome = TaskOutcome.Failed;
            note = ex.Message;
        }
        finally
        {
            var counts = $"{_checked:N0} videos checked, {_generated:N0} created, {_failed:N0} failed";
            await run.CompleteAsync(outcome, note is null ? counts : $"{note}. {counts}");
            _discovering = false;
            _pending.Clear();
            _pass = null;
        }
    }

    /// <summary>Adds the videos of the pass without (current) trickplay to <see cref="_pending"/>, one signal each.</summary>
    private void Discover(TrickplayOptions settings, SemaphoreSlim found, int workers, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var video in EnumerateVideos(settings, cancellationToken))
            {
                Interlocked.Increment(ref _checked);
                var source = video.FullName;
                var folder = GetCacheFolder(source);
                var info = ReadInfo(folder);
                if (!IsFresh(info, video, settings)
                    && !_running.ContainsKey(source)
                    && !_requested.Contains(source)
                    && _pending.Add(source))
                    found.Release();
            }
        }
        finally
        {
            // Every worker wakes up to see that nothing more is coming.
            _discovering = false;
            found.Release(workers);
        }
    }

    private async Task WorkAsync(TrickplayOptions settings, SemaphoreSlim found, CancellationToken cancellationToken)
    {
        while (true)
        {
            // Read before looking at the queue: once discovery is over, nothing is added to it any more.
            var complete = !_discovering;
            if (_pending.TryTake(out var source))
            {
                // Every library's passes together make at most Parallelism videos at once (FfmpegLimits).
                var gate = _limits.Trickplay(settings.Parallelism);
                await gate.WaitAsync(cancellationToken);
                try
                {
                    await TryEnsureAsync(source, settings, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }

                continue;
            }

            if (complete)
                return;
            // Extra signals (videos moved to the player's queue) only cost a turn of the loop.
            await found.WaitAsync(cancellationToken);
        }
    }

    private async Task QueueLoopAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            await _queueSignal.WaitAsync(stoppingToken);
            while (_requested.TryTake(out var source))
            {
                if (!IsAvailable)
                    continue;
                // Cancel() stops the video being made, not the loop.
                using var work = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _queueWork.Token);
                try
                {
                    await TryEnsureAsync(source, Options, work.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                }
            }
        }
    }

    private async Task TryEnsureAsync(string source, TrickplayOptions settings, CancellationToken cancellationToken)
    {
        var video = new FileInfo(source);
        if (!video.Exists)
            return;

        try
        {
            await EnsureAsync(video, settings, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Trickplay failed for {File}", source);
        }
    }

    /// <summary>Videos of the configured folders, skipping the folders and hidden items that scans skip.</summary>
    private IEnumerable<FileInfo> EnumerateVideos(TrickplayOptions settings, CancellationToken cancellationToken)
    {
        var ignored = new HashSet<string>(_options.CurrentValue.Scan.IgnoredDirectories, StringComparer.OrdinalIgnoreCase);
        var roots = RootFolders(settings);
        roots.Reverse();
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<FileSystemInfo> entries;
            try
            {
                entries = [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", _browser.EnumerationOptions)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                _logger.LogDebug(ex, "Trickplay skips {Folder}", folder);
                continue;
            }

            // Sub-folders pushed in reverse so they are visited in name order, after this folder's videos.
            foreach (var child in entries.OfType<DirectoryInfo>().Where(d => !ignored.Contains(d.Name)).OrderByDescending(d => d.Name, NaturalComparer.Instance))
                pending.Push(child.FullName);
            foreach (var file in entries.OfType<FileInfo>().OrderBy(f => f.Name, NaturalComparer.Instance))
            {
                if (_classifier.Classify(file.Extension) == ItemKind.Video)
                    yield return file;
            }
        }
    }

    /// <summary>The configured folders that exist, without those inside another one.</summary>
    private List<string> RootFolders(TrickplayOptions settings)
    {
        if (settings.Folders.Count == 0)
            return [Path.TrimEndingDirectorySeparator(_paths.Root)];

        var folders = settings.Folders
            .Select(f => _paths.TryResolve(f, out var full) ? full : null)
            .OfType<string>()
            .Where(Directory.Exists)
            .Distinct(LibraryPaths.PathComparer)
            .ToList();
        return [.. folders.Where(f => !folders.Any(other => !LibraryPaths.PathComparer.Equals(f, other) && IsWithin(f, other)))];
    }

    private bool IsInFolders(string source, TrickplayOptions settings) =>
        settings.Folders.Count == 0
        || settings.Folders.Any(f => _paths.TryResolve(f, out var folder) && IsWithin(source, folder));

    private static bool IsWithin(string path, string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return path.StartsWith(folder, LibraryPaths.PathComparison);
    }

    private async Task EnsureAsync(FileInfo video, TrickplayOptions settings, CancellationToken cancellationToken)
    {
        var folder = GetCacheFolder(video.FullName);
        var info = ReadInfo(folder);
        if (IsFresh(info, video, settings) || !_running.TryAdd(video.FullName, DateTimeOffset.Now))
            return;

        var started = Stopwatch.GetTimestamp();
        try
        {
            var error = await GenerateAsync(video, folder, settings, cancellationToken);
            if (error is null)
                Interlocked.Increment(ref _generated);
            else
                Interlocked.Increment(ref _failed);
            Remember(new TrickplayResult(
                Relative(video.FullName),
                error is null,
                error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] + "…" : error,
                DateTimeOffset.Now,
                Stopwatch.GetElapsedTime(started).TotalSeconds));
        }
        finally
        {
            _running.TryRemove(video.FullName, out _);
        }
    }

    private void Remember(TrickplayResult result)
    {
        lock (_recent)
        {
            _recent.Enqueue(result);
            while (_recent.Count > RecentCount)
                _recent.Dequeue();
        }
    }

    /// <summary>
    /// Two ffmpeg runs: the frames, as separate pictures (their number is only known once they are out), then the
    /// sheets. Built in a temporary folder that replaces the previous trickplay when complete.
    /// </summary>
    /// <returns>null when the trickplay was made, otherwise why not.</returns>
    private async Task<string?> GenerateAsync(FileInfo video, string folder, TrickplayOptions settings, CancellationToken cancellationToken)
    {
        var temp = folder + ".tmp";
        var frames = Path.Combine(temp, "frames");
        var timeout = TimeSpan.FromMinutes(Math.Max(1, settings.TimeoutMinutes));
        try
        {
            DeleteFolder(temp);
            Directory.CreateDirectory(frames);

            List<string> extract = ["-hide_banner", "-loglevel", "error", "-nostdin"];
            if (settings.KeyFramesOnly)
                extract.AddRange(["-skip_frame", "nokey"]);
            // round=up: frame n is the picture at n × interval (the default rounding would take it half an interval early).
            extract.AddRange([
                "-i", video.FullName,
                "-an", "-sn", "-dn",
                "-vf", $"fps=1/{settings.IntervalSeconds}:round=up,scale='min({settings.Width},iw)':-2",
                "-q:v", "5",
                Path.Combine(frames, "%05d.jpg"),
            ]);
            var (exitCode, errors) = await _ffmpeg.RunAsync(extract, timeout, cancellationToken: cancellationToken);
            var pictures = Directory.GetFiles(frames, "*.jpg");
            if (pictures.Length == 0 || JpegSize(pictures[0]) is not var (tileWidth, tileHeight))
                return Fail($"ffmpeg exited with {exitCode}: {errors.Trim()}");

            List<string> tile =
            [
                "-hide_banner", "-loglevel", "error", "-nostdin",
                "-framerate", "1", "-i", Path.Combine(frames, "%05d.jpg"),
                "-vf", $"tile={settings.Columns}x{settings.Rows}",
                "-q:v", "5",
                "-start_number", "0", Path.Combine(temp, "%d.jpg"),
            ];
            (exitCode, errors) = await _ffmpeg.RunAsync(tile, timeout, cancellationToken: cancellationToken);
            var sheets = (pictures.Length + settings.Columns * settings.Rows - 1) / (settings.Columns * settings.Rows);
            if (exitCode != 0 || !File.Exists(Path.Combine(temp, $"{sheets - 1}.jpg")))
                return Fail($"ffmpeg could not tile the frames ({exitCode}): {errors.Trim()}");

            DeleteFolder(frames);
            var manifest = new TrickplayManifest(
                settings.IntervalSeconds, pictures.Length, settings.Columns, settings.Rows, tileWidth, tileHeight, sheets, DateTime.UtcNow.Ticks);
            // A failure only costs the fingerprint: the trickplay is fine for the player.
            await FingerprintAsync(temp, manifest, settings, cancellationToken);
            WriteInfo(temp, new StoredInfo(video.Length, video.LastWriteTimeUtc.Ticks, ImageKey(settings), manifest));
            DeleteFolder(folder);
            Directory.Move(temp, folder);
            _logger.LogDebug("Trickplay of {File}: {Count} frames on {Sheets} sheets", video.FullName, pictures.Length, sheets);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryDeleteFolder(temp);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trickplay generation failed for {File}", video.FullName);
            return Fail(ex.Message);
        }

        string Fail(string reason)
        {
            _logger.LogInformation("No trickplay for {File}: {Reason}", video.FullName, reason);
            TryDeleteFolder(temp);
            try
            {
                // Remembered, so that the next passes skip the file until it or the settings change.
                Directory.CreateDirectory(folder);
                foreach (var sheet in Directory.EnumerateFiles(folder, "*.jpg"))
                    File.Delete(sheet);
                WriteInfo(folder, new StoredInfo(video.Length, video.LastWriteTimeUtc.Ticks, FailureKey(settings), null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Cannot record the trickplay failure of {File}", video.FullName);
            }

            return reason;
        }
    }

    /// <summary>
    /// Writes <c>fingerprint.bin</c> into <paramref name="folder"/> from its sheets: ffmpeg cuts them back into frames
    /// (<c>untile</c>) and shrinks each one to the 9×8 grey pixels of <see cref="VideoFingerprint"/>. Cheap next to
    /// the trickplay itself: only a few JPEG sheets are decoded. A failure only costs the fingerprint: the video is
    /// left out of similar-video comparisons until its trickplay is made again.
    /// </summary>
    private async Task FingerprintAsync(string folder, TrickplayManifest manifest, TrickplayOptions settings, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, FingerprintFile);
        string error;
        try
        {
            List<string> arguments =
            [
                "-hide_banner", "-loglevel", "error", "-nostdin",
                "-start_number", "0", "-i", Path.Combine(folder, "%d.jpg"),
                "-vf", $"format=gray,untile={manifest.Columns}x{manifest.Rows},scale={VideoFingerprint.PixelWidth}:{VideoFingerprint.PixelHeight}:flags=area",
                "-frames:v", manifest.Count.ToString(),
                "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1",
            ];
            using var output = new MemoryStream();
            var (exitCode, errors) = await _ffmpeg.RunAsync(arguments, TimeSpan.FromMinutes(Math.Max(1, settings.TimeoutMinutes)), output, cancellationToken);
            if (exitCode == 0 && output.Length == (long)manifest.Count * VideoFingerprint.FrameBytes)
            {
                VideoFingerprint.FromGreyFrames(manifest.Interval, output.GetBuffer().AsSpan(0, (int)output.Length), manifest.Count).Write(path);
                return;
            }

            error = $"ffmpeg could not read the frames back ({exitCode}, {output.Length / VideoFingerprint.FrameBytes} of {manifest.Count}): {errors.Trim()}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
        }

        _logger.LogWarning("No fingerprint for the trickplay in {Folder}: {Reason}", folder, error);
    }

    /// <summary>
    /// Fingerprint of the video at <paramref name="sourceFullPath"/>, as it is now, from its trickplay; null when it has
    /// none (no trickplay yet, made of an older version of the file, or the fingerprint could not be made).
    /// </summary>
    public VideoFingerprint? GetFingerprint(string sourceFullPath)
    {
        var video = new FileInfo(sourceFullPath);
        var folder = GetCacheFolder(sourceFullPath);
        if (!video.Exists || ReadInfo(folder) is not { Manifest: { } manifest } info || !SameSource(info, video))
            return null;
        return VideoFingerprint.Read(Path.Combine(folder, FingerprintFile), manifest.Interval) is { Count: > 0 } fingerprint ? fingerprint : null;
    }

    /// <summary>Contents of <c>info.json</c>; <see cref="Manifest"/> is null when generation failed.</summary>
    private sealed record StoredInfo(long SourceSize, long SourceModified, string Key, TrickplayManifest? Manifest);

    private static StoredInfo? ReadInfo(string folder)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(folder, InfoFile));
            return JsonSerializer.Deserialize<StoredInfo>(stream, Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteInfo(string folder, StoredInfo info)
    {
        var path = Path.Combine(folder, InfoFile);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(info, Json));
        File.Move(temp, path, overwrite: true);
    }

    private static bool SameSource(StoredInfo info, FileInfo video) =>
        info.SourceSize == video.Length && info.SourceModified == video.LastWriteTimeUtc.Ticks;

    private static bool IsFresh(StoredInfo? info, FileInfo video, TrickplayOptions settings) =>
        info is not null
        && SameSource(info, video)
        && info.Key == (info.Manifest is null ? FailureKey(settings) : ImageKey(settings));

    /// <summary>Settings that change the pictures: trickplay made with other values is made again.</summary>
    private static string ImageKey(TrickplayOptions s) => $"{s.IntervalSeconds}|{s.Width}|{s.Columns}|{s.Rows}|{s.KeyFramesOnly}";

    /// <summary>A failed video is tried again when any setting that could help changes (e.g. a longer time limit).</summary>
    private static string FailureKey(TrickplayOptions s) => $"{ImageKey(s)}|{s.TimeoutMinutes}";

    /// <summary>Settings that decide which videos a pass handles, and how.</summary>
    private static string PassSettings(SfeOptions o) => JsonSerializer.Serialize(new
    {
        o.Trickplay,
        o.FileTypes.Videos,
        o.Scan.IgnoredDirectories,
        o.ShowHiddenItems,
    });

    private void OnOptionsChanged(SfeOptions options, string? name)
    {
        var settings = PassSettings(options);
        if (Interlocked.Exchange(ref _passSettings, settings) == settings)
            return;

        // Restarted with the new settings: videos already done are skipped quickly.
        _passTrigger = TaskTrigger.Automatic;
        _passScheduleId = null;
        _passRequested = true;
        try
        {
            _pass?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The pass just ended.
        }

        _passSignal.Release();
    }

    /// <summary>Width and height of a JPEG image, from its start-of-frame segment.</summary>
    internal static (int Width, int Height)? JpegSize(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
            return null;

        while (true)
        {
            int marker;
            do
            {
                marker = stream.ReadByte();
            }
            while (marker == 0xFF);

            if (marker < 0)
                return null;

            // Markers without a length.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
                continue;

            var length = (stream.ReadByte() << 8) | stream.ReadByte();
            // SOF0-SOF15, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                stream.ReadByte();      // precision
                var height = (stream.ReadByte() << 8) | stream.ReadByte();
                var width = (stream.ReadByte() << 8) | stream.ReadByte();
                return width > 0 && height > 0 ? (width, height) : null;
            }

            if (length < 2)
                return null;
            stream.Seek(length - 2, SeekOrigin.Current);

            // Each segment is followed by the 0xFF of the next marker.
            if (stream.ReadByte() != 0xFF)
                return null;
        }
    }

    private static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private void TryDeleteFolder(string folder)
    {
        try
        {
            DeleteFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Cannot delete {Folder}", folder);
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _passSignal.Dispose();
        _queueSignal.Dispose();
    }
}
