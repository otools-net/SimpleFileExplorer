namespace Otools.Net.Sfe.Client;

/// <summary>
/// How much of a connection the library's index holds, so how much a search of it can find
/// (<see cref="SfeConnectionSearchPage.Coverage"/>).
/// </summary>
public enum SfeIndexCoverage
{
    /// <summary>A word this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>Not indexed in this library yet: nothing is found; its first sync has started.</summary>
    None,

    /// <summary>Being listed, or not synced for a while: some files may be missing or old.</summary>
    Partial,

    /// <summary>The whole connection, as its last sync saw it.</summary>
    Complete,
}
