using System.Collections.Generic;
using System.Threading;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// One page of a search of a connection's files, with the cursor of the next and how much the index holds.
/// </summary>
public sealed class SfeConnectionSearchPage
{
    /// <summary>The files found, in the order asked; paths relative to the connection's root.</summary>
    public IReadOnlyList<SfeItem> Items { get; init; } = [];

    /// <summary>Whether more results follow.</summary>
    public bool HasMore { get; init; }

    /// <summary>
    /// The cursor of the next page, given back to
    /// <see cref="SfeConnectionSearch.GetPageAsync(string, SfeSearchOptions?, string?, CancellationToken)"/>
    /// with the same options; null on the last page.
    /// </summary>
    public string? Next { get; init; }

    /// <summary>
    /// On the first page only: how many files match, counted up to the server's limit (10,000); null on the others.
    /// </summary>
    public int? Total { get; init; }

    /// <summary>Whether more files match than <see cref="Total"/> (the count stopped at the limit).</summary>
    public bool TotalCapped { get; init; }

    /// <summary>
    /// How much of the connection the index holds: with <see cref="SfeIndexCoverage.None"/> nothing is found yet
    /// (the first sync has started; <see cref="SfeConnection.GetIndexAsync"/> tells when it ends).
    /// </summary>
    public SfeIndexCoverage Coverage { get; init; }
}
