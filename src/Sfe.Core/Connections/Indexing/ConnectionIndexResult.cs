namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>A sync ended (<see cref="ConnectionIndexer.SyncAsync"/>).</summary>
/// <param name="Files">Files in the index now.</param>
/// <param name="Folders">Folders in the index now.</param>
/// <param name="Listed">A full listing ran (the first, or after the provider reset its cursor).</param>
/// <param name="Changes">Changes read from the provider's cursor.</param>
/// <param name="TooLong">Items left out: their path is longer than the index stores.</param>
public sealed record ConnectionIndexResult(long Files, long Folders, bool Listed, long Changes, long TooLong)
{
    /// <summary>"1,204 files and 37 folders indexed (listed), 12 changes, 2 left out (path too long)".</summary>
    public string Summary()
    {
        var text = $"{Files:N0} files and {Folders:N0} folders indexed";
        if (Listed)
            text += " (listed in full)";
        if (Changes > 0)
            text += $", {Changes:N0} changes";
        if (TooLong > 0)
            text += $", {TooLong:N0} left out (path too long)";
        return text;
    }
}
