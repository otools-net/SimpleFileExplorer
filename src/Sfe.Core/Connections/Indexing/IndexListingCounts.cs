namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>The folders and files an index listing shows (what folder rules hide left out).</summary>
internal sealed record IndexListingCounts(int Folders, int Files)
{
    public int Total => Folders + Files;
}
