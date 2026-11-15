using System.Collections.Concurrent;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The places where the next pages of the library's index listings begin (<see cref="IndexListingPlace"/>), so that
/// scrolling a folder reads each page by key from the last row of the one before, while the routes keep their
/// offsets. Bounded: at most <see cref="MaxPlaces"/>, each for <see cref="Lifetime"/>; a page without its place is
/// read by offset.
/// </summary>
public sealed class IndexListingPlaces(TimeProvider clock)
{
    /// <summary>Places kept; past it the old ones go, then all.</summary>
    public const int MaxPlaces = 1024;

    /// <summary>How long a place is used.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<IndexListingKey, IndexListingPlace> _places = new();

    internal IndexListingPlace? Find(IndexListingKey key) =>
        _places.TryGetValue(key, out var place) && clock.GetUtcNow() - place.Kept <= Lifetime ? place : null;

    internal void Keep(IndexListingKey key, IndexListingRow last, IndexListingCounts counts)
    {
        if (_places.Count >= MaxPlaces)
            Trim();
        _places[key] = new IndexListingPlace(last, counts, clock.GetUtcNow());
    }

    private void Trim()
    {
        var now = clock.GetUtcNow();
        foreach (var (key, place) in _places)
        {
            if (now - place.Kept > Lifetime)
                _places.TryRemove(key, out _);
        }
        if (_places.Count >= MaxPlaces)
            _places.Clear();
    }
}
