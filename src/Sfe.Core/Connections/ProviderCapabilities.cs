namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// What a kind of storage can do by itself. SFE does the rest (or hides the function): the routes, the transfer jobs
/// and the web client read these instead of knowing the kinds.
/// </summary>
public sealed record ProviderCapabilities
{
    /// <summary>
    /// A part of a file can be read; without it a file is always served whole (no seeking in a video).
    /// </summary>
    public bool RangedReads { get; init; }

    /// <summary>
    /// An upload goes to the storage part by part; without it the parts wait in a file of the server until the
    /// last one.
    /// </summary>
    public bool UploadsInParts { get; init; }

    /// <summary>The largest part of an upload, in bytes; null when the storage sets no limit.</summary>
    public long? MaxUploadPart { get; init; }

    /// <summary>Copies inside the storage without the bytes passing through the server.</summary>
    public bool ServerSideCopy { get; init; }

    /// <summary>Moves and renames inside the storage without the bytes passing through the server.</summary>
    public bool ServerSideMove { get; init; }

    public bool Thumbnails { get; init; }

    /// <summary>Tells what changed since a cursor, so an index follows it without listing everything again.</summary>
    public bool ChangeCursor { get; init; }

    /// <summary>
    /// The name of the hash it gives for a file's content (<c>dropbox</c>, <c>md5</c>, <c>sha256</c>); null when it
    /// gives none. Two files are compared only by hashes of the same name.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// Keeps what is deleted for a while (a trash, versions): a delete there can be undone at the provider.
    /// </summary>
    public bool OwnTrash { get; init; }

    /// <summary>Names differing only by case are the same item.</summary>
    public bool CaseInsensitiveNames { get; init; }

    /// <summary>A folder never holds two items of the same name (false for storages addressed by ids).</summary>
    public bool UniqueNames { get; init; } = true;

    /// <summary>Folders have a modification date.</summary>
    public bool FolderDates { get; init; }

    /// <summary>Tells the space used and left.</summary>
    public bool FreeSpace { get; init; }
}
