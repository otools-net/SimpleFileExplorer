namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// The sync of one connection in a library's index (<see cref="Connections.Indexing.ConnectionIndexer"/>): written
/// with each page of the listing or of the changes, so that a restart goes on where it stopped.
/// </summary>
public sealed class RemoteIndexState
{
    /// <summary>The connection (server-wide id, in the accounts' database: no foreign key).</summary>
    public int ConnectionId { get; set; }

    /// <summary>
    /// The connection's root folder when it was indexed: another root, or another account at the provider
    /// (<see cref="AccountKey"/>), and the rows are dropped and listed again.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>A hash of the provider's account it was indexed from (never the account itself).</summary>
    public string? AccountKey { get; set; }

    public RemoteIndexPhase Phase { get; set; }

    /// <summary>The number of the current full listing: rows it did not see are dropped when it ends.</summary>
    public int Pass { get; set; }

    /// <summary>The provider's change cursor: what changed since it comes next. Null without one.</summary>
    public string? ChangeCursor { get; set; }

    /// <summary>UTC: when <see cref="ChangeCursor"/> was first taken (a reset one tells how old it was).</summary>
    public DateTime? CursorTaken { get; set; }

    /// <summary>The folder being listed (<see cref="RemoteDirectory.Id"/>) when a listing stopped in it.</summary>
    public long? WalkFolderId { get; set; }

    /// <summary>The provider's cursor of the next page of <see cref="WalkFolderId"/>.</summary>
    public string? WalkCursor { get; set; }

    /// <summary>UTC: when the current (or last) full listing began.</summary>
    public DateTime? ListingStarted { get; set; }

    /// <summary>UTC: when the index last became current (a listing or the changes read to their end).</summary>
    public DateTime? LastSync { get; set; }

    /// <summary>Files in the index (counted when a sync ends; grows while a listing goes).</summary>
    public long Files { get; set; }

    /// <summary>Folders in the index (counted when a sync ends; grows while a listing goes).</summary>
    public long Folders { get; set; }

    /// <summary>Items left out since the current listing began: their path is longer than the index stores.</summary>
    public int TooLong { get; set; }

    /// <summary>What stopped the last sync; null when it ended well.</summary>
    public string? LastError { get; set; }

    /// <summary>UTC: when the connection was first indexed in this library.</summary>
    public DateTime Created { get; set; }
}
