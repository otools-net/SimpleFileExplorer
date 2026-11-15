namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>Where the index of a connection stands in the library of the request.</summary>
/// <param name="Indexed">The library has begun indexing it (a first sync started).</param>
/// <param name="Running">A sync runs now.</param>
/// <param name="Listing">A full listing is under way: the index doesn't hold everything yet.</param>
/// <param name="Files">Files in the index when the last sync ended.</param>
/// <param name="Folders">Folders in the index when the last sync ended.</param>
/// <param name="LastSync">When the index was last brought up to date.</param>
/// <param name="TooLong">Items left out since the last full listing began: their path is too long.</param>
/// <param name="LastError">What stopped the last sync; null when it ended well.</param>
/// <param name="Activity">What the running sync is doing, in words.</param>
public sealed record ConnectionIndexStatus(
    bool Indexed,
    bool Running,
    bool Listing,
    long Files,
    long Folders,
    DateTimeOffset? LastSync,
    int TooLong,
    string? LastError,
    string? Activity
);
