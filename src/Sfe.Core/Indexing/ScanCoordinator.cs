using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.FileTracking;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Core.Indexing;

/// <summary>Runs at most one index scan at a time in the background and reports its progress.</summary>
public sealed class ScanCoordinator(
    DirectoryIndexer indexer,
    RelinkService relinker,
    LibraryPaths paths,
    TaskHistory history,
    ILogger<ScanCoordinator> logger
) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _current;
    private ScanProgress _progress = new();
    private ScanState _state = ScanState.Idle;
    private string? _path;
    private string? _fullPath;

    /// <summary>The paths file operations are changing now (<see cref="TryBeginOperation"/>).</summary>
    private readonly List<string> _operations = [];

    /// <summary>Completed when an operation ends: a scan waiting for them looks again.</summary>
    private TaskCompletionSource? _operationEnded;
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
                    _state,
                    _path,
                    _startedAt,
                    _finishedAt,
                    _progress.Directories,
                    _state == ScanState.Running ? _progress.Queued : 0,
                    _progress.Files,
                    _progress.Removed,
                    _progress.Errors,
                    current is null ? null : paths.ToRelative(current) ?? current,
                    _message,
                    _progress.Relink?.Relinked ?? 0,
                    _progress.Relink?.Remaining
                );
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
            _fullPath = fullPath;
            _startedAt = DateTimeOffset.Now;
            _finishedAt = null;
            _message = null;

            var progress = _progress;
            var token = _current.Token;
            var relative = _path;
            Completion = Task.Run(
                () => RunAsync(fullPath, relative, progress, trigger, scheduleId, token),
                CancellationToken.None
            );
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

    private async Task RunAsync(
        string fullPath,
        string? relativePath,
        ScanProgress progress,
        TaskTrigger trigger,
        int? scheduleId,
        CancellationToken cancellationToken
    )
    {
        // From here on, what the scan logs goes into the run's log.
        var run = history.Begin(BackgroundTaskKind.Scan, relativePath ?? string.Empty, trigger, scheduleId);
        logger.LogInformation("Scan of {Path} started", fullPath);
        ScanState outcome;
        string? message = null;
        try
        {
            await WaitForOperationsAsync(fullPath, cancellationToken);
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
            "Scan of {Path} {Outcome}: {Directories} folders, {Files} files indexed, {Removed} removed, "
                + "{Relinked} relinked, {Errors} errors",
            fullPath,
            outcome,
            progress.Directories,
            progress.Files,
            progress.Removed,
            progress.Relink?.Relinked ?? 0,
            progress.Errors
        );

        await run.CompleteAsync(
            outcome switch
            {
                ScanState.Completed => TaskOutcome.Completed,
                ScanState.Cancelled => TaskOutcome.Cancelled,
                _ => TaskOutcome.Failed,
            },
            outcome == ScanState.Failed ? message : Describe(progress)
        );
    }

    /// <summary>The counts of a scan in words, for the task list and the run history.</summary>
    internal static string Describe(ScanProgress progress) =>
        Describe(
            progress.Directories,
            0,
            progress.Files,
            progress.Removed,
            progress.Relink?.Relinked ?? 0,
            progress.Errors
        );

    internal static string Describe(int directories, int queued, int files, int removed, int relinked, int errors) =>
        string.Join(
            ", ",
            new[]
            {
                $"{directories:N0} folders scanned",
                queued > 0 ? $"{queued:N0} waiting" : null,
                $"{files:N0} files indexed",
                removed > 0 ? $"{removed:N0} removed" : null,
                relinked > 0 ? $"{relinked:N0} moved files relinked" : null,
                errors > 0 ? $"{errors:N0} errors" : null,
            }.OfType<string>()
        );

    /// <summary>
    /// Removes a folder, its sub-folders and their files from the index (they stay on the disk). Tagged files are kept
    /// as missing files with their tags (when relinking is on): scanning the folder again gives the tags back.
    /// </summary>
    /// <returns>What the index held of the folder, or null while a scan runs (it could index the folder again right
    /// away).</returns>
    public async Task<IndexedFolderStats?> RemoveFromIndexAsync(
        string relativePath,
        CancellationToken cancellationToken = default
    )
    {
        var fullPath = paths.Resolve(relativePath);
        lock (_gate)
        {
            if (_state == ScanState.Running)
                return null;
        }

        var removed = await indexer.RemoveTreeAsync(fullPath, cancellationToken);
        logger.LogInformation(
            "{Path} removed from the index: {Directories} folders, {Files} files",
            fullPath,
            removed.Directories,
            removed.Files
        );
        return removed;
    }

    /// <summary>
    /// Begins a file operation (a rename, a new folder…) changing <paramref name="fullPaths"/>, for as long as the
    /// result is not disposed. Refused (null) while a scan runs over one of them: the scan could index the old place
    /// after the change, or remember a moved tagged file as missing. A scan starting meanwhile waits for it to end.
    /// </summary>
    public IDisposable? TryBeginOperation(params string[] fullPaths)
    {
        lock (_gate)
        {
            if (
                _state == ScanState.Running
                && _fullPath is { } scanned
                && fullPaths.Any(path => Overlaps(path, scanned))
            )
            {
                return null;
            }

            _operations.AddRange(fullPaths);
            return new Operation(this, fullPaths);
        }
    }

    private void EndOperation(string[] fullPaths)
    {
        TaskCompletionSource? ended;
        lock (_gate)
        {
            foreach (var path in fullPaths)
                _operations.Remove(path);
            ended = _operationEnded;
            _operationEnded = null;
        }
        ended?.TrySetResult();
    }

    private async Task WaitForOperationsAsync(string fullPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task ended;
            lock (_gate)
            {
                if (!_operations.Any(path => Overlaps(path, fullPath)))
                    return;
                ended = (
                    _operationEnded ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
                ).Task;
            }
            await ended.WaitAsync(cancellationToken);
        }
    }

    /// <summary>One path is the other or inside it.</summary>
    private static bool Overlaps(string a, string b) =>
        LibraryPaths.IsSameOrInside(a, b) || LibraryPaths.IsSameOrInside(b, a);

    private sealed class Operation(ScanCoordinator coordinator, string[] fullPaths) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                coordinator.EndOperation(fullPaths);
        }
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
