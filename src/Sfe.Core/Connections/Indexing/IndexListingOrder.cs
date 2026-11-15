using System.Linq.Expressions;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The order of an index listing's rows and the rows after a given one, as queries the database answers from its
/// indexes on <c>(ConnectionId, DirectoryId, …, NameKey, FullName)</c>: by the sort's value, then by the name as
/// listings sort it (<see cref="Data.RemoteFile.NameKey"/>: numbers in order, case and accents as
/// <see cref="NaturalComparer"/>), then by the key, all in the direction asked (so one index is walked one way). The
/// rows after a row are read in seeks, one per level from the deepest (<see cref="After"/>): an <c>OR</c> of them is
/// not a seek on SQL Server. String comparisons use the column's collation, the one <c>ORDER BY</c> uses too.
/// </summary>
internal static class IndexListingOrder
{
    /// <summary>The sort a listing of files reads with: what browsing doesn't sort by is the name.</summary>
    public static SortField OfFiles(SortField sort) =>
        sort is SortField.Modified or SortField.Size or SortField.Type ? sort : SortField.Name;

    /// <summary>
    /// The sort of the folders: by name, by date for <see cref="SortField.Modified"/>; size and type sorts list
    /// them by ascending name (they have neither), as <see cref="RemoteSort"/> does.
    /// </summary>
    public static (SortField Sort, bool Descending) OfFolders(SortField sort, bool descending) =>
        OfFiles(sort) switch
        {
            SortField.Modified => (SortField.Modified, descending),
            SortField.Name => (SortField.Name, descending),
            _ => (SortField.Name, false),
        };

    public static IQueryable<IndexListingRow> Ordered(
        this IQueryable<IndexListingRow> rows,
        SortField sort,
        bool descending
    ) =>
        sort switch
        {
            SortField.Modified => ByName(First(rows, r => r.Modified, descending), descending),
            SortField.Size => ByName(First(rows, r => r.Size, descending), descending),
            SortField.Type => ByName(First(rows, r => r.Extension, descending), descending),
            _ => descending
                ? rows.OrderByDescending(r => r.NameKey).ThenByDescending(r => r.FullName)
                : rows.OrderBy(r => r.NameKey).ThenBy(r => r.FullName),
        };

    /// <summary>
    /// The rows after <paramref name="last"/>, as queries to read one after the other: those of its name (then of
    /// its sort value) with a later key, those of its sort value with a later name, those with a later value.
    /// </summary>
    public static IEnumerable<IQueryable<IndexListingRow>> After(
        this IQueryable<IndexListingRow> rows,
        SortField sort,
        bool descending,
        IndexListingRow last
    )
    {
        var tied = SameValue(rows, sort, last);
        var (key, name) = (last.FullName, last.NameKey);
        var sameName = tied.Where(r => r.NameKey == name);
        yield return descending
            ? sameName.Where(r => string.Compare(r.FullName, key) < 0)
            : sameName.Where(r => string.Compare(r.FullName, key) > 0);
        yield return descending
            ? tied.Where(r => string.Compare(r.NameKey, name) < 0)
            : tied.Where(r => string.Compare(r.NameKey, name) > 0);
        if (OfFiles(sort) != SortField.Name)
            yield return Past(rows, sort, descending, last);
    }

    /// <summary>The rows with <paramref name="last"/>'s sort value (all of them for the name sort).</summary>
    private static IQueryable<IndexListingRow> SameValue(
        IQueryable<IndexListingRow> rows,
        SortField sort,
        IndexListingRow last
    )
    {
        var (modified, size, extension) = (last.Modified, last.Size, last.Extension);
        return sort switch
        {
            SortField.Modified => rows.Where(r => r.Modified == modified),
            SortField.Size => rows.Where(r => r.Size == size),
            SortField.Type => rows.Where(r => r.Extension == extension),
            _ => rows,
        };
    }

    /// <summary>
    /// The rows whose sort value comes after <paramref name="last"/>'s. Folders without a date come first going up
    /// and last going down, as the databases order nulls.
    /// </summary>
    private static IQueryable<IndexListingRow> Past(
        IQueryable<IndexListingRow> rows,
        SortField sort,
        bool descending,
        IndexListingRow last
    )
    {
        var (modified, size, extension) = (last.Modified, last.Size, last.Extension);
        return (sort, descending) switch
        {
            (SortField.Modified, false) => modified is { } after
                ? rows.Where(r => r.Modified > after)
                : rows.Where(r => r.Modified != null),
            (SortField.Modified, true) => modified is { } before
                ? rows.Where(r => r.Modified < before || r.Modified == null)
                : rows.Where(r => false),
            (SortField.Size, false) => rows.Where(r => r.Size > size),
            (SortField.Size, true) => rows.Where(r => r.Size < size),
            (SortField.Type, false) => rows.Where(r => string.Compare(r.Extension, extension) > 0),
            _ => rows.Where(r => string.Compare(r.Extension, extension) < 0),
        };
    }

    private static IOrderedQueryable<IndexListingRow> First<T>(
        IQueryable<IndexListingRow> rows,
        Expression<Func<IndexListingRow, T>> value,
        bool descending
    ) => descending ? rows.OrderByDescending(value) : rows.OrderBy(value);

    private static IQueryable<IndexListingRow> ByName(IOrderedQueryable<IndexListingRow> rows, bool descending) =>
        descending
            ? rows.ThenByDescending(r => r.NameKey).ThenByDescending(r => r.FullName)
            : rows.ThenBy(r => r.NameKey).ThenBy(r => r.FullName);
}
