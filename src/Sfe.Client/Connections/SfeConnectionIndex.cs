using System;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Where the library's index of a connection's files stands (<see cref="SfeConnection.GetIndexAsync"/>): what it
/// holds, its last sync, the one running.
/// </summary>
public sealed class SfeConnectionIndex
{
    /// <summary>Whether the library has begun indexing it (its first sync started when first opened there).</summary>
    public bool Indexed { get; init; }

    /// <summary>Whether a sync is running.</summary>
    public bool Running { get; init; }

    /// <summary>Whether a full listing is under way: the index doesn't hold everything yet.</summary>
    public bool Listing { get; init; }

    /// <summary>The files in the index when the last sync ended.</summary>
    public long Files { get; init; }

    /// <summary>The folders in the index when the last sync ended.</summary>
    public long Folders { get; init; }

    /// <summary>When the index was last brought up to date; null before the first sync ends.</summary>
    public DateTimeOffset? LastSync { get; init; }

    /// <summary>Items left out since the last full listing began: their path is longer than the index keeps.</summary>
    public int TooLong { get; init; }

    /// <summary>What stopped the last sync, for people; null when it ended well.</summary>
    public string? LastError { get; init; }

    /// <summary>What the running sync does, for people; null when none runs.</summary>
    public string? Activity { get; init; }
}
