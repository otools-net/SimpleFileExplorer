namespace Otools.Net.Sfe.Core.Files;

/// <summary>Whether the file system lets the server put files in a folder now (<see cref="DiskWrites"/>).</summary>
public enum DiskWrite
{
    /// <summary>It does, or it can't be told (only trying tells: shares, errors).</summary>
    Accepted,

    /// <summary>The volume is read-only: mounted so, a disc, write-protected media.</summary>
    ReadOnlyVolume,

    /// <summary>The server's account may not write there (permissions, ACLs).</summary>
    Refused,
}
