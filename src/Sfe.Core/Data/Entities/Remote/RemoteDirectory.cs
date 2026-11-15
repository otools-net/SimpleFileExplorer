namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// A folder of a connection in a library's index (agents/docs/development/connections-index-plan.md), kept by
/// <see cref="Connections.Indexing.ConnectionIndexer"/>. Folders are known by their path: only files carry tags, so
/// a folder renamed at the provider is a new row, its files following it by their provider ids.
/// </summary>
public sealed class RemoteDirectory
{
    public long Id { get; set; }

    /// <summary>The connection (server-wide id, in the accounts' database: no foreign key).</summary>
    public int ConnectionId { get; set; }

    /// <summary>The folder it is in; null for the connection's root folder.</summary>
    public long? ParentId { get; set; }

    public RemoteDirectory? Parent { get; set; }

    /// <summary>The provider's id of the folder, when it gives one (to tell where a folder went).</summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// The path from the connection's root, compared as the provider compares names (lowered when it ignores case):
    /// the key. Empty for the root.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>The path as the provider spells it, to show and to ask the provider with.</summary>
    public string DisplayPath { get; set; } = string.Empty;

    /// <summary>The folder's name as the provider spells it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The name (with its extension) as listings sort it (<see cref="Library.NaturalComparer.KeyOf"/>): ordinal order
    /// of this key is the natural order of the names. Null on rows indexed before it existed, until a sync fills it.
    /// </summary>
    public string? NameKey { get; set; }

    /// <summary>UTC; null when the provider keeps no date for folders (Dropbox).</summary>
    public DateTime? LastWriteTime { get; set; }

    /// <summary>All its items are in the index (a listing reached it, or it came by the change cursor).</summary>
    public bool Listed { get; set; }

    /// <summary>A full listing still has to list it (resumed there after a restart).</summary>
    public bool Pending { get; set; }

    /// <summary>The listing pass that last saw it (<see cref="RemoteIndexState.Pass"/>).</summary>
    public int Pass { get; set; }

    /// <summary>Gone at the provider: deleted at the end of the sync unless it comes back.</summary>
    public bool Removed { get; set; }

    public List<RemoteDirectory> Directories { get; set; } = [];
    public List<RemoteFile> Files { get; set; } = [];

    public override string ToString() => DisplayPath;
}
