namespace Otools.Net.Sfe.Core.Connections;

/// <summary>What became of an item changed through SFE (<see cref="ConnectionItemChange"/>).</summary>
public enum ConnectionItemChangeKind
{
    /// <summary>Made, uploaded, or written over.</summary>
    Put,

    /// <summary>Copied there by the provider (a folder's items are not told: the next sync brings them).</summary>
    Copied,

    /// <summary>Renamed or moved, inside the connection (a folder with everything in it).</summary>
    Moved,

    /// <summary>Deleted, or moved out of the connection (a folder with everything in it).</summary>
    Removed,
}
