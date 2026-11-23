namespace Otools.Net.Sfe.Core.Library.Watching;

/// <summary>
/// One listing's hold on a watched folder (<see cref="FolderWatchers.Watch"/>): the folder is watched while someone
/// holds it. Disposing it lets go (twice is harmless).
/// </summary>
public sealed class FolderWatch : IDisposable
{
    /// <summary>Nothing watched: the folder doesn't exist, or too many are watched already.</summary>
    public static readonly FolderWatch None = new(null, null);

    private readonly FolderWatchers? _owner;
    private readonly WatchedFolder? _folder;
    private int _released;

    internal FolderWatch(FolderWatchers? owner, WatchedFolder? folder) => (_owner, _folder) = (owner, folder);

    /// <summary>The folder is watched (or polled) for this hold.</summary>
    public bool IsActive => _folder is not null;

    public void Dispose()
    {
        if (_folder is not null && Interlocked.Exchange(ref _released, 1) == 0)
            _owner!.Release(_folder);
    }
}
