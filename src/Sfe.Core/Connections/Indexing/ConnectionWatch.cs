namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// One listing's hold on a connection's folder (<see cref="ConnectionWatchers.Watch"/>): the connection's changes are
/// read while someone holds one of its folders. Disposing it lets go (twice is harmless).
/// </summary>
public sealed class ConnectionWatch : IDisposable
{
    /// <summary>Nothing watched: too many folders are watched already, or the library is closing.</summary>
    public static readonly ConnectionWatch None = new(null, 0, "");

    private readonly ConnectionWatchers? _owner;
    private readonly int _connectionId;
    private readonly string _folder;
    private int _released;

    internal ConnectionWatch(ConnectionWatchers? owner, int connectionId, string folder) =>
        (_owner, _connectionId, _folder) = (owner, connectionId, folder);

    /// <summary>The folder is followed for this hold.</summary>
    public bool IsActive => _owner is not null;

    public void Dispose()
    {
        if (_owner is not null && Interlocked.Exchange(ref _released, 1) == 0)
            _owner.Release(_connectionId, _folder);
    }
}
