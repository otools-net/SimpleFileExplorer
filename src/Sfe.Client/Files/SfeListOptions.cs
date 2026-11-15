namespace Otools.Net.Sfe.Client;

/// <summary>
/// How a folder is listed by
/// <see cref="SfeFiles.ListAsync(string, SfeListOptions?, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed class SfeListOptions
{
    /// <summary>The order; by name when not set.</summary>
    public SfeSortField Sort { get; set; } = SfeSortField.Name;

    /// <summary>Whether the order is reversed.</summary>
    public bool Descending { get; set; }

    /// <summary>How many items each request fetches (the server keeps it within its limits). Default 500.</summary>
    public int PageSize { get; set; } = 500;
}
