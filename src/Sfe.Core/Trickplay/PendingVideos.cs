using System.Diagnostics.CodeAnalysis;
using Sfe.Core.Paths;

namespace Sfe.Core.Trickplay;

/// <summary>
/// Videos waiting for their trickplay, in order: taken from the front, but also looked up and removed by path, so
/// that the listings can show which videos are waiting and a video opened in the player can move ahead.
/// </summary>
internal sealed class PendingVideos
{
    private readonly Lock _lock = new();
    private readonly LinkedList<string> _order = [];
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(LibraryPaths.PathComparer);

    public int Count
    {
        get
        {
            lock (_lock)
                return _order.Count;
        }
    }

    /// <returns>false when the video is already waiting.</returns>
    public bool Add(string path)
    {
        lock (_lock)
        {
            if (_nodes.ContainsKey(path))
                return false;
            _nodes[path] = _order.AddLast(path);
            return true;
        }
    }

    public bool Contains(string path)
    {
        lock (_lock)
            return _nodes.ContainsKey(path);
    }

    public bool Remove(string path)
    {
        lock (_lock)
        {
            if (!_nodes.Remove(path, out var node))
                return false;
            _order.Remove(node);
            return true;
        }
    }

    public bool TryTake([MaybeNullWhen(false)] out string path)
    {
        lock (_lock)
        {
            path = _order.First?.Value;
            if (path is null)
                return false;
            _order.RemoveFirst();
            _nodes.Remove(path);
            return true;
        }
    }

    /// <summary>The next <paramref name="count"/> videos, in order.</summary>
    public List<string> First(int count)
    {
        lock (_lock)
            return [.. _order.Take(count)];
    }

    public void Clear()
    {
        lock (_lock)
        {
            _order.Clear();
            _nodes.Clear();
        }
    }
}
