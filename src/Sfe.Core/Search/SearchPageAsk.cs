using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Search;

/// <summary>A page of search results as <see cref="SearchPages.ReadAsync"/> reads it.</summary>
/// <param name="Cursor">Where the page goes on from; null for the first page.</param>
/// <param name="Count">Rows to read at most.</param>
public sealed record SearchPageAsk(SortField Sort, bool Descending, SearchCursor? Cursor, int Count);
