using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>One page of a search's results, with the cursor of the next.</summary>
public sealed class SfeSearchPage
{
    /// <summary>The files found, in the order asked.</summary>
    public IReadOnlyList<SfeItem> Items { get; init; } = [];

    /// <summary>Whether more results follow.</summary>
    public bool HasMore { get; init; }

    /// <summary>
    /// The cursor of the next page, given back to
    /// <see cref="SfeSearch.GetPageAsync(string, SfeSearchOptions?, string?, System.Threading.CancellationToken)"/>
    /// with the same options; null on the last page.
    /// </summary>
    public string? Next { get; init; }

    /// <summary>
    /// On the first page only: how many files match, counted up to the server's limit (10,000); null on the others.
    /// </summary>
    public int? Total { get; init; }

    /// <summary>Whether more files match than <see cref="Total"/> (the count stopped at the limit).</summary>
    public bool TotalCapped { get; init; }
}
