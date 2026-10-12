using Microsoft.Extensions.Logging;
using Sfe.Core.FileTracking;
using Sfe.Core.Paths;
using Sfe.Core.Tasks;

namespace Sfe.Core.Indexing;

public enum ScanState
{
    Idle,
    Running,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>Thread-safe counters updated while a scan runs.</summary>
public sealed class ScanProgress
{
    private int _directories;
    private int _queued;
    private int _files;
    private int _removed;
    private int _errors;

    public string? CurrentDirectory { get; set; }
    public int Directories => _directories;

    /// <summary>Folders found but not scanned yet. The total is unknown up front: folders are discovered level by level.</summary>
    public int Queued => _queued;
    public int Files => _files;
    public int Removed => _removed;
    public int Errors => _errors;

    /// <summary>Missing files relinked after the scan; null until that pass ran.</summary>
    public RelinkPassResult? Relink { get; set; }

    public void AddDirectory() => Interlocked.Increment(ref _directories);
    public void AddQueued(int count) => Interlocked.Add(ref _queued, count);
    public void AddFile() => Interlocked.Increment(ref _files);
    public void AddRemoved(int count) => Interlocked.Add(ref _removed, count);
    public void AddError() => Interlocked.Increment(ref _errors);
}

/// <param name="FilesIndexed">New or changed files written to the index.</param>
/// <param name="Removed">Files and folders removed from the index because they no longer exist.</param>
/// <param name="DirectoriesQueued">Folders found and still waiting to be scanned.</param>
/// <param name="Relinked">Missing (tagged) files found again after the scan, moved or renamed, and given back their tags.</param>
/// <param name="Missing">Tagged files no longer found, waiting to be relinked (after the scan; null before).</param>
public sealed record ScanStatus(
    ScanState State,
    string? Path,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int DirectoriesScanned,
    int DirectoriesQueued,
    int FilesIndexed,
    int Removed,
    int Errors,
    string? CurrentDirectory,
    string? Message,
    int Relinked = 0,
    int? Missing = null);

/// <summary>What the index holds of a folder, before removing it from the index.</summary>
/// <param name="Indexed">The folder itself is in the index.</param>
/// <param name="Directories">The folder and its sub-folders in the index.</param>
/// <param name="TaggedFiles">Of <paramref name="Files"/>, those with tags.</param>
public sealed record IndexedFolderStats(bool Indexed, int Directories, int Files, int TaggedFiles);

/// <summary>Runs at most one index scan at a time in the background and reports its progress.</summary>
public sealed class ScanCoordinator(DirectoryIndexer indexer, RelinkService relinker, LibraryPaths paths, TaskHistory history, ILogger<ScanCoordinator> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _current;
    private ScanProgress _progress = new();
    private ScanState _state = ScanState.Idle;
    private string? _path;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;
    private string? _message;

    public Task? Completion { get; private set; }

    public ScanStatus Status
    {
        get
        {
            lock (_gate)
            {
                var current = _progress.CurrentDirectory;
                return new ScanStatus(
                    _state, _path, _startedAt, _finishedAt,
                    _progress.Directories, _state == ScanState.Running ? _progress.Queued : 0,
                    _progress.Files, _progress.Removed, _progress.Errors,
                    current is null ? null : paths.ToRelative(current) ?? current,
                    _message,
                    _progress.Relink?.Relinked ?? 0,
                    _progress.Relink?.Remaining);
            }
        }
    }

    /// <summary>Starts scanning <paramref name="relativePath"/> recursively.</summary>
    /// <param name="scheduleId">Schedule starting it, for the run history.</param>
    /// <returns>false when a scan is already running.</returns>
    public bool TryStart(string relativePath, TaskTrigger trigger = TaskTrigger.Manual, int? scheduleId = null)
    {
        var fullPath = paths.Resolve(relativePath);
        lock (_gate)
        {
            if (_state == ScanState.Running)
                return false;

            _current?.Dispose();
            _current = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _progress = new ScanProgress();
            _state = ScanState.Running;
            _path = paths.ToRelative(fullPath);
            _startedAt = DateTimeOffset.Now;
            _finishedAt = null;
            _message = null;

            var progress = _progress;
            var token = _current.Token;
            var relative = _path;
            Completion = Task.Run(() => RunAsync(fullPath, relative, progress, trigger, scheduleId, token), CancellationToken.None);
            return true;
        }
    }

    public bool Cancel()
    {
        lock (_gate)
        {
            if (_state != ScanState.Running)
                return false;

            _current?.Cancel();
            return true;
        }
    }

    private async Task RunAsync(string fullPath, string? relativePath, ScanProgress progress, TaskTrigger trigger, int? scheduleId, CancellationToken cancellationToken)
    {
        // From here on, what the scan logs goes into the run's log.
        var run = history.Begin(BackgroundTaskKind.Scan, relativePath ?? string.Empty, trigger, scheduleId);
        logger.LogInformation("Scan of {Path} started", fullPath);
        ScanState outcome;
        string? message = null;
        try
        {
            await indexer.IndexAsync(fullPath, recursive: true, progress, cancellationToken);
            // Files moved during the scan are only all indexed now, at their old and new places: match them up.
            await RelinkAsync(progress, cancellationToken);
            outcome = ScanState.Completed;
        }
        catch (OperationCanceledException)
        {
            outcome = ScanState.Cancelled;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scan of {Path} failed", fullPath);
            outcome = ScanState.Failed;
            message = ex.Message;
        }

        lock (_gate)
        {
            _state = outcome;
            _message = message;
            _finishedAt = DateTimeOffset.Now;
            progress.CurrentDirectory = null;
        }

        logger.LogInformation(
            "Scan of {Path} {Outcome}: {Directories} folders, {Files} files indexed, {Removed} removed, {Relinked} relinked, {Errors} errors",
            fullPath, outcome, progress.Directories, progress.Files, progress.Removed, progress.Relink?.Relinked ?? 0, progress.Errors);

        await run.CompleteAsync(outcome switch
        {
            ScanState.Completed => TaskOutcome.Completed,
            ScanState.Cancelled => TaskOutcome.Cancelled,
            _ => TaskOutcome.Failed,
        }, outcome == ScanState.Failed ? message : Describe(progress));
    }

    /// <summary>The counts of a scan in words, for the task list and the run history.</summary>
    internal static string Describe(ScanProgress progress) => Describe(
        progress.Directories, 0, progress.Files, progress.Removed, progress.Relink?.Relinked ?? 0, progress.Errors);

    internal static string Describe(int directories, int queued, int files, int removed, int relinked, int errors) =>
        string.Join(", ", new[]
        {
            $"{directories:N0} folders scanned",
            queued > 0 ? $"{queued:N0} waiting" : null,
            $"{files:N0} files indexed",
            removed > 0 ? $"{removed:N0} removed" : null,
            relinked > 0 ? $"{relinked:N0} moved files relinked" : null,
            errors > 0 ? $"{errors:N0} errors" : null,
        }.OfType<string>());

    /// <summary>
    /// Removes a folder, its sub-folders and their files from the index (they stay on the disk). Tagged files are kept
    /// as missing files with their tags (when relinking is on): scanning the folder again gives the tags back.
    /// </summary>
    /// <returns>What the index held of the folder, or null while a scan runs (it could index the folder again right away).</returns>
    public async Task<IndexedFolderStats?> RemoveFromIndexAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = paths.Resolve(relativePath);
        lock (_gate)
        {
            if (_state == ScanState.Running)
                return null;
        }

        var removed = await indexer.RemoveTreeAsync(fullPath, cancellationToken);
        logger.LogInformation("{Path} removed from the index: {Directories} folders, {Files} files", fullPath, removed.Directories, removed.Files);
        return removed;
    }

    private async Task RelinkAsync(ScanProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            progress.Relink = await relinker.RunAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The index itself is up to date: report the scan as done, the next scan (or the button) tries again.
            progress.AddError();
            logger.LogWarning(ex, "Relinking moved files failed");
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _current?.Dispose();
    }
}
