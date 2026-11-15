namespace Otools.Net.Sfe.Client;

/// <summary>Where a search looks and how its results come (<see cref="SfeSearch"/>).</summary>
public sealed class SfeSearchOptions
{
    /// <summary>
    /// The folder searched, with its sub-folders; <c>""</c> (the default): the whole library. A folder that isn't
    /// there finds nothing.
    /// </summary>
    public string Folder { get; set; } = "";

    /// <summary>The order; by path when not set.</summary>
    public SfeSearchSort Sort { get; set; } = SfeSearchSort.Path;

    /// <summary>Whether the order is reversed.</summary>
    public bool Descending { get; set; }

    /// <summary>How many results each request fetches (the server keeps it within its limits). Default 500.</summary>
    public int PageSize { get; set; } = 500;
}
