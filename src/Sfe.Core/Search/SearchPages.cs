using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Search;

/// <summary>
/// Pages of search results read by key ("after this sort value and path") rather than by offset, on any index of
/// files (<see cref="ISearchableFile{TSelf}"/>): an index on the sort column gives the order, so a page reads the
/// matches it returns instead of sorting every match and skipping the previous pages
/// (agents/docs/features/search-sort.md).
/// </summary>
public static class SearchPages
{
    /// <summary>
    /// The page following <paramref name="cursor"/> (the first one without it), <paramref name="count"/> rows at most,
    /// each made by <paramref name="project"/>.
    /// </summary>
    /// <remarks>
    /// Sorted by duration or resolution (or by a sort name some rows lack), the files with a known value come first (a
    /// range of that column's index), then those without one by ascending path (the path index): neither direction
    /// starts with every file that has none, and no page sorts every match. A page may hold the end of the first part
    /// and the start of the second.
    /// </remarks>
    public static async Task<List<TRow>> ReadAsync<T, TRow>(
        IQueryable<T> files,
        SearchPageAsk ask,
        Expression<Func<T, TRow>> project,
        CancellationToken cancellationToken
    )
        where T : class, ISearchableFile<T>
    {
        var (sort, descending, cursor, count) = ask;
        if (!HasTwoParts<T>(sort))
        {
            return await ToRowsAsync(
                Order(cursor is null ? files : After(files, sort, descending, cursor), sort, descending),
                count,
                project,
                cancellationToken
            );
        }

        var (known, unknown) = Parts(files, sort);
        var rows = new List<TRow>();
        if (cursor is not { Unknown: true })
        {
            rows = await ToRowsAsync(
                Order(cursor is null ? known : After(known, sort, descending, cursor), sort, descending),
                count,
                project,
                cancellationToken
            );
        }

        if (rows.Count < count)
        {
            var path = cursor is { Unknown: true } ? cursor.FullName : null;
            var rest = path is null ? unknown : unknown.Where(f => string.Compare(f.FullName, path) > 0);
            rows.AddRange(
                await ToRowsAsync(rest.OrderBy(f => f.FullName), count - rows.Count, project, cancellationToken)
            );
        }

        return rows;
    }

    private static bool HasTwoParts<T>(SortField sort)
        where T : class, ISearchableFile<T> =>
        sort is SortField.Duration or SortField.Resolution || sort == SortField.Name && T.SortNameMayBeMissing;

    /// <summary>The files whose value of the sort is known, and the others.</summary>
    private static (IQueryable<T> Known, IQueryable<T> Unknown) Parts<T>(IQueryable<T> files, SortField sort)
        where T : class, ISearchableFile<T> =>
        sort switch
        {
            SortField.Duration => (
                files.Where(f => f.Duration > 0),
                files.Where(f => f.Duration == null || f.Duration <= 0)
            ),
            SortField.Resolution => (
                files.Where(f => f.Pixels > 0),
                files.Where(f => f.Pixels == null || f.Pixels <= 0)
            ),
            _ => (
                files.Where(WithName<T>((name, f) => name != null)),
                files.Where(WithName<T>((name, f) => name == null))
            ),
        };

    private static Task<List<TRow>> ToRowsAsync<T, TRow>(
        IQueryable<T> files,
        int count,
        Expression<Func<T, TRow>> project,
        CancellationToken cancellationToken
    ) => files.Take(count).Select(project).ToListAsync(cancellationToken);

    /// <summary>By the sort value, ascending or descending, then always by ascending path.</summary>
    private static IQueryable<T> Order<T>(IQueryable<T> files, SortField sort, bool descending)
        where T : class, ISearchableFile<T> =>
        (sort, descending) switch
        {
            (SortField.Name, false) => files.OrderBy(T.SortName).ThenBy(f => f.FullName),
            (SortField.Name, true) => files.OrderByDescending(T.SortName).ThenBy(f => f.FullName),
            (SortField.Modified, false) => files.OrderBy(f => f.LastWriteTime).ThenBy(f => f.FullName),
            (SortField.Modified, true) => files.OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.FullName),
            (SortField.Size, false) => files.OrderBy(f => f.Size).ThenBy(f => f.FullName),
            (SortField.Size, true) => files.OrderByDescending(f => f.Size).ThenBy(f => f.FullName),
            (SortField.Type, false) => files.OrderBy(f => f.Extension).ThenBy(f => f.FullName),
            (SortField.Type, true) => files.OrderByDescending(f => f.Extension).ThenBy(f => f.FullName),
            (SortField.Duration, false) => files.OrderBy(f => f.Duration).ThenBy(f => f.FullName),
            (SortField.Duration, true) => files.OrderByDescending(f => f.Duration).ThenBy(f => f.FullName),
            (SortField.Resolution, false) => files.OrderBy(f => f.Pixels).ThenBy(f => f.FullName),
            (SortField.Resolution, true) => files.OrderByDescending(f => f.Pixels).ThenBy(f => f.FullName),
            (_, false) => files.OrderBy(f => f.FullName),
            (_, true) => files.OrderByDescending(f => f.FullName),
        };

    /// <summary>The files following the cursor in <see cref="Order"/>.</summary>
    /// <remarks>
    /// String comparisons become SQL comparisons with the column's collation, the one ORDER BY uses too.
    /// </remarks>
    private static IQueryable<T> After<T>(IQueryable<T> files, SortField sort, bool descending, SearchCursor cursor)
        where T : class, ISearchableFile<T>
    {
        var path = cursor.FullName;
        var text = cursor.Text;
        var number = cursor.Number;
        var seconds = cursor.Seconds;
        var time = sort == SortField.Modified ? new DateTime(number) : default;
        return (sort, descending) switch
        {
            (SortField.Name, false) => files.Where(
                WithName<T>(
                    (name, f) => string.Compare(name, text) > 0 || name == text && string.Compare(f.FullName, path) > 0
                )
            ),
            (SortField.Name, true) => files.Where(
                WithName<T>(
                    (name, f) => string.Compare(name, text) < 0 || name == text && string.Compare(f.FullName, path) > 0
                )
            ),
            (SortField.Modified, false) => files.Where(f =>
                f.LastWriteTime > time || f.LastWriteTime == time && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Modified, true) => files.Where(f =>
                f.LastWriteTime < time || f.LastWriteTime == time && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Size, false) => files.Where(f =>
                f.Size > number || f.Size == number && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Size, true) => files.Where(f =>
                f.Size < number || f.Size == number && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Type, false) => files.Where(f =>
                string.Compare(f.Extension, text) > 0 || f.Extension == text && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Type, true) => files.Where(f =>
                string.Compare(f.Extension, text) < 0 || f.Extension == text && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Duration, false) => files.Where(f =>
                f.Duration > seconds || f.Duration == seconds && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Duration, true) => files.Where(f =>
                f.Duration < seconds || f.Duration == seconds && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Resolution, false) => files.Where(f =>
                f.Pixels > number || f.Pixels == number && string.Compare(f.FullName, path) > 0
            ),
            (SortField.Resolution, true) => files.Where(f =>
                f.Pixels < number || f.Pixels == number && string.Compare(f.FullName, path) > 0
            ),
            (_, false) => files.Where(f => string.Compare(f.FullName, path) > 0),
            (_, true) => files.Where(f => string.Compare(f.FullName, path) < 0),
        };
    }

    /// <summary>
    /// <paramref name="test"/> with its first parameter replaced by the file's sort name
    /// (<see cref="ISearchableFile{TSelf}.SortName"/>): one predicate whatever column each index sorts names by.
    /// </summary>
    private static Expression<Func<T, bool>> WithName<T>(Expression<Func<string?, T, bool>> test)
        where T : class, ISearchableFile<T>
    {
        var file = test.Parameters[1];
        var sortName = T.SortName;
        var name = new ParameterReplacer(sortName.Parameters[0], file).Visit(sortName.Body);
        var body = new ParameterReplacer(test.Parameters[0], name).Visit(test.Body);
        return Expression.Lambda<Func<T, bool>>(body, file);
    }

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
