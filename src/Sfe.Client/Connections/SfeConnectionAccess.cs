namespace Otools.Net.Sfe.Client;

/// <summary>How far the caller uses a connection (its folder rules may narrow it below).</summary>
public enum SfeConnectionAccess
{
    /// <summary>A level this version of the library doesn't know (a newer server's), or none.</summary>
    Unknown,

    /// <summary>Read only: list, read and download.</summary>
    Read,

    /// <summary>Read and change: upload, make, rename, move, copy, delete.</summary>
    Write,
}
