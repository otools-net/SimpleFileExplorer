using System.Linq.Expressions;
using Sfe.Core.Paths;

namespace Sfe.Core.Access;

public static class PathFilterQueries
{
    /// <summary>
    /// The rows whose full path (selected by <paramref name="fullName"/>) <paramref name="filter"/> includes; all of
    /// them for a null filter or <see cref="PathFilter.Everything"/> (no condition added: the query stays as it was).
    /// </summary>
    public static IQueryable<T> WhereVisible<T>(
        this IQueryable<T> query,
        PathFilter? filter,
        Expression<Func<T, string>> fullName,
        LibraryPaths paths
    ) => filter is null || filter.IsEverything ? query : query.Where(filter.ToPredicate(fullName, paths));
}
