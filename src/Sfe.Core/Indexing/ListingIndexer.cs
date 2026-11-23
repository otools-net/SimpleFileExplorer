using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Indexing;

/// <summary>
/// Scan as you go (agents/docs/features/indexing.md): a library folder someone lists is indexed in the background, that
/// folder only (its files, and its sub-folders gone from the disk), at most once per <see cref="Interval"/>, one
/// folder at a time, never while a scan or a file operation works on it (<see cref="ScanCoordinator"/>). When the index
/// changed, the folder's open listings are told (<see cref="LibraryBrowser.Invalidate"/>): their marks, tags and
/// resolutions are read again.
/// </summary>
public sealed class ListingIndexer : IDisposable
{
    /// <summary>A folder is indexed again at most this often.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>Folders waiting their turn; further ones are left for a later listing.</summary>
    internal const int MaxQueued = 64;

    /// <summary>
    /// Folders whose last indexing is remembered; past it, those older than the interval are forgotten.
    /// </summary>
    internal const int MaxRemembered = 10_000;

    private readonly DirectoryIndexer _indexer;
    private readonly ScanCoordinator _scans;
    private readonly LibraryBrowser _browser;
    private readonly LibraryPaths _paths;
    private readonly IOptionsMonitor<SfeOptions> _options;
    private readonly ILogger<ListingIndexer> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastIndexed = new(LibraryPaths.PathComparer);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(MaxQueued) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true }
    );

    public ListingIndexer(
        DirectoryIndexer indexer,
        ScanCoordinator scans,
        LibraryBrowser browser,
        LibraryPaths paths,
        IOptionsMonitor<SfeOptions> options,
        ILogger<ListingIndexer> logger
    )
    {
        (_indexer, _scans, _browser, _paths, _options, _logger) = (indexer, scans, browser, paths, options, logger);
        _ = Task.Run(RunAsync, CancellationToken.None);
    }

    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// A queued folder (full path) was dealt with: indexed (true when its index changed: the listings were told), or
    /// left (false). Raised by the background loop; for tests.
    /// </summary>
    internal event Action<string, bool>? Done;

    /// <summary>
    /// The folder <paramref name="folder"/> (root-relative) was just listed: queues it to be indexed unless it was
    /// lately or the setting is off. Never waits.
    /// </summary>
    public void Listed(string folder)
    {
        if (!_options.CurrentValue.Scan.IndexWhenListed || !_paths.TryResolve(folder, out var full))
            return;
        full = Path.TrimEndingDirectorySeparator(full);
        var now = Clock.GetUtcNow();
        lock (_gate)
        {
            if (_lastIndexed.TryGetValue(full, out var at) && now - at < Interval)
                return;
            if (_lastIndexed.Count >= MaxRemembered)
                ForgetOlderThan(now - Interval);
            _lastIndexed[full] = now;
        }
        // Too many waiting: a later listing asks again.
        if (!_queue.Writer.TryWrite(full))
            Forget(full);
    }

    private void ForgetOlderThan(DateTimeOffset limit)
    {
        foreach (var old in _lastIndexed.Where(e => e.Value < limit).Select(e => e.Key).ToList())
            _lastIndexed.Remove(old);
    }

    private void Forget(string fullPath)
    {
        lock (_gate)
        {
            _lastIndexed.Remove(fullPath);
        }
    }

    private async Task RunAsync()
    {
        var token = _shutdown.Token;
        try
        {
            await foreach (var folder in _queue.Reader.ReadAllAsync(token))
            {
                // Apart: `Done?.Invoke(folder, await …)` would skip the indexing when no one listens.
                var changed = await IndexAsync(folder, token);
                Done?.Invoke(folder, changed);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <returns>Whether the index of the folder changed.</returns>
    private async Task<bool> IndexAsync(string folder, CancellationToken cancellationToken)
    {
        // A scan over it indexes it anyway; an operation changes it (and tells it): a later listing asks again.
        using var operation = _scans.TryBeginFolderIndex(folder);
        if (operation is null)
        {
            Forget(folder);
            return false;
        }

        var progress = new ScanProgress();
        try
        {
            await _indexer.IndexAsync(folder, recursive: false, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Indexing the listed folder {Folder} failed", folder);
            return false;
        }

        if (progress.Files == 0 && progress.Removed == 0)
            return false;
        _logger.LogDebug(
            "Listed folder {Folder} indexed: {Files} files indexed, {Removed} removed",
            folder,
            progress.Files,
            progress.Removed
        );
        _browser.Invalidate(folder);
        return true;
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        // Not disposed: the loop may still be reading its token.
        _shutdown.Cancel();
    }
}
