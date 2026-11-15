namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>How much of a connection the library's index holds, which is what a search of it can find.</summary>
public enum RemoteSearchCoverage
{
    /// <summary>Not indexed in this library yet (or of another root folder or account): nothing is found.</summary>
    None,

    /// <summary>
    /// Being listed (the first sync, or again after the provider forgot its cursor), or not synced for a while: what
    /// is found is what the index holds, which may miss files or hold some gone since.
    /// </summary>
    Partial,

    /// <summary>Synced past its full listing, recently (<see cref="RemoteIndexListings.MaxAge"/>).</summary>
    Complete,
}
