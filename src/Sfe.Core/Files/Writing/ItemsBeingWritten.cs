using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The library's files and folders being written by SFE (copies, transfers into the library, compressions,
/// extractions: agents/docs/features/partial-files.md) and how far each is, for open listings. Writers count their
/// bytes on their <see cref="ItemWrite"/> (no lock, nothing raised per write); a timer, running only while something
/// is written, tells <see cref="FolderProgressed"/> at most every <see cref="Interval"/> per folder, when an item moved
/// or ended, and every <see cref="Heartbeat"/> anyway (a listing opened meanwhile learns of a write that stalls).
/// </summary>
public sealed class ItemsBeingWritten(LibraryPaths paths) : IDisposable
{
    /// <summary>The time between two progress events of a folder.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(300);

    /// <summary>Ticks after which a folder is told even when nothing moved.</summary>
    private const int HeartbeatTicks = 10;

    /// <summary>The longest a folder with writes in progress goes untold.</summary>
    public static TimeSpan Heartbeat => Interval * HeartbeatTicks;

    private readonly Lock _gate = new();
    private readonly List<ItemWrite> _items = [];
    private readonly HashSet<string> _ended = new(LibraryPaths.PathComparer);
    private Timer? _timer;
    private int _ticks;
    private bool _disposed;

    /// <summary>No timer: <see cref="Tick"/> is called by hand (tests).</summary>
    internal bool Manual { get; init; }

    /// <summary>
    /// A folder (root-relative) whose items being written moved on, started or ended: <see cref="In"/> tells them.
    /// Raised by the timer's thread.
    /// </summary>
    public event Action<string>? FolderProgressed;

    /// <summary>
    /// Starts telling the write of <paramref name="partial"/> (its full path, <c>name.sfe-partial</c>), which takes the
    /// name <paramref name="name"/> when done; <paramref name="total"/>: its bytes when known (0 otherwise). Disposing
    /// the result ends it. Outside the library: a write nobody is told of.
    /// </summary>
    public ItemWrite Begin(string partial, string name, long total)
    {
        var folder = paths.ToRelative(Path.GetDirectoryName(partial) ?? string.Empty);
        var write = new ItemWrite(this, folder ?? string.Empty, Path.GetFileName(partial), name, total);
        if (folder is null)
            return write;
        lock (_gate)
        {
            if (_disposed)
                return write;
            _items.Add(write);
            if (!Manual)
                _timer ??= new Timer(_ => Tick(), null, Interval, Interval);
        }
        return write;
    }

    /// <summary>The items being written now in <paramref name="folder"/> (root-relative), oldest first.</summary>
    public IReadOnlyList<ItemWriting> In(string folder)
    {
        lock (_gate)
        {
            return
            [
                .. _items
                    .Where(w => LibraryPaths.PathComparer.Equals(w.Folder, folder))
                    .Select(w => new ItemWriting(w.PartialName, w.Name, w.Done, w.Total)),
            ];
        }
    }

    internal void End(ItemWrite write)
    {
        lock (_gate)
        {
            if (_items.Remove(write))
                _ended.Add(write.Folder);
        }
    }

    /// <summary>
    /// Tells the folders whose items moved or ended since the last tick (all of them every <see cref="Heartbeat"/>);
    /// stops the timer once nothing is written. Called by the timer, and by tests.
    /// </summary>
    internal void Tick()
    {
        List<string> told;
        lock (_gate)
        {
            var all = ++_ticks % HeartbeatTicks == 0;
            told = [.. _ended];
            _ended.Clear();
            foreach (var write in _items.Where(w => w.TakeMoved() || all))
            {
                if (!told.Contains(write.Folder, LibraryPaths.PathComparer))
                    told.Add(write.Folder);
            }
            if (_items.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
        foreach (var folder in told)
            Raise(folder);
    }

    private void Raise(string folder)
    {
        if (FolderProgressed is not { } handlers)
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

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
