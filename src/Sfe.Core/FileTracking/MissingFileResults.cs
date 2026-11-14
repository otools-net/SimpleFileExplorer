namespace Otools.Net.Sfe.Core.FileTracking;

/// <param name="Total">Missing files in all; only on the first page.</param>
/// <param name="Next">Value of the <c>after</c> parameter that requests the next page; null on the last page.</param>
/// <param name="Enabled">Relinking is on (<c>FileTracking:Relink:Enabled</c>).</param>
public sealed record MissingFileResults(
    IReadOnlyList<MissingFileInfo> Items,
    int? Total,
    bool HasMore,
    int? Next,
    bool Enabled
);
