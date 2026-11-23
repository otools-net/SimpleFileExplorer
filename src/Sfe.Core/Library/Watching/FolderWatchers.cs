using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Library.Watching;

/// <summary>
/// Changes made to the library's folders outside SFE (another program, the network share's other users), followed
/// only in the folders someone has open (agents/docs/features/change-feed.md): each listing holds a
/// <see cref="FolderWatch"/>, and a folder is watched while one is held. The system's watcher tells changes; where it
/// can't (a network mount outside Windows, a watcher refused or failing) the folder is polled. A change empties the
/// folder's kept listings and raises <see cref="Changed"/>; SFE's own changes, already told by
/// <see cref="LibraryBrowser.FolderChanged"/>, are not told again.
/// </summary>
public sealed class FolderWatchers : IDisposable
{
    /// <summary>The most folders of one library watched at once: further ones are not.</summary>
    public const int MaxFolders = 256;

    private readonly LibraryPaths _paths;
    private readonly LibraryBrowser _browser;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, WatchedFolder> _folders = new(LibraryPaths.PathComparer);
    private bool _disposed;
    private bool _toldFull;

    public FolderWatchers(LibraryPaths paths, LibraryBrowser browser, ILogger<FolderWatchers> logger)
    {
        (_paths, _browser, Logger) = (paths, browser, logger);
        browser.FolderChanged += OnOwnChange;
    }

    internal ILogger Logger { get; }

    internal FolderWatchTimings Timings { get; init; } = FolderWatchTimings.Default;

    /// <summary>Every folder polled, none given to the system's watcher (tests).</summary>
    internal bool PollOnly { get; init; }

    /// <summary>
    /// A watched folder (root-relative) changed outside SFE; its kept listings are already forgotten. Raised by a
    /// timer's thread.
    /// </summary>
    public event Action<string>? Changed;

    /// <summary>The folders watched now (root-relative).</summary>
    public IReadOnlyList<string> Folders
    {
        get
        {
            lock (_gate)
            {
                return [.. _folders.Keys];
            }
        }
    }

    /// <summary>Whether <paramref name="folder"/> is polled rather than watched (null when not followed).</summary>
    public bool? IsPolled(string folder)
    {
        lock (_gate)
        {
            return _folders.TryGetValue(folder, out var watched) ? watched.Polled : null;
        }
    }

    /// <summary>
    /// Follows <paramref name="folder"/> (root-relative) until the result is disposed. Nothing is followed for a folder
    /// outside the library or missing, or past <see cref="MaxFolders"/>.
    /// </summary>
    public FolderWatch Watch(string folder)
    {
        lock (_gate)
        {
            if (_disposed)
                return FolderWatch.None;
            if (!_folders.TryGetValue(folder, out var watched))
            {
                if (!_paths.TryResolve(folder, out var full) || !Directory.Exists(full) || IsFull())
                    return FolderWatch.None;
                watched = new WatchedFolder(this, folder, full);
                _folders[folder] = watched;
                watched.Start();
            }
            watched.Leases++;
            return new FolderWatch(this, watched);
        }
    }

    /// <summary>An item SFE never tells of: one it is writing, or its work folder at the library's top.</summary>
    internal static bool IsIgnored(string name, bool top) =>
        LibraryPaths.IsPartial(name)
        || (top && string.Equals(name, LibraryPaths.WorkFolderName, StringComparison.OrdinalIgnoreCase));

    internal void Release(WatchedFolder watched)
    {
        lock (_gate)
        {
            if (--watched.Leases > 0)
                return;
            if (_folders.TryGetValue(watched.Folder, out var current) && current == watched)
                _folders.Remove(watched.Folder);
        }
        watched.Dispose();
    }

    /// <summary>
    /// <paramref name="watched"/> changed, first seen at <paramref name="since"/> (<see cref="Environment.TickCount64"/>):
    /// told, unless SFE changed it itself about then.
    /// </summary>
    internal void Tell(WatchedFolder watched, long since)
    {
        var own = Interlocked.Read(ref watched.LastOwnChange);
        if (own != 0 && Math.Abs(since - own) < Timings.OwnChangeWindow.TotalMilliseconds)
            return;
        if (_browser.Forget(watched.FullPath) is not { } folder || Changed is not { } handlers)
            return;
        // A listener failing (a feed gone) must not stop the others.
        foreach (var handler in handlers.GetInvocationList().Cast<Action<string>>())
        {
            try
            {
                handler(folder);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
    }

    private bool IsFull()
    {
        if (_folders.Count < MaxFolders)
            return false;
        if (!_toldFull)
        {
            Logger.LogWarning(
                "{Count} folders are watched for changes made outside SFE already: no more are",
                MaxFolders
            );
            _toldFull = true;
        }
        return true;
    }

    private void OnOwnChange(string folder)
    {
        WatchedFolder? watched;
        lock (_gate)
        {
            watched = _folders.GetValueOrDefault(folder);
        }
        watched?.OwnChange();
    }

    public void Dispose()
    {
        List<WatchedFolder> folders;
        lock (_gate)
        {
            _disposed = true;
            folders = [.. _folders.Values];
            _folders.Clear();
        }
        _browser.FolderChanged -= OnOwnChange;
        folders.ForEach(f => f.Dispose());
    }
}
