using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Search;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Where a search of a connection's index looks, as conditions on the rows' keys: the folder searched, and what
/// folder rules leave the asker (the regions of <see cref="RemoteSearchAsk.Visible"/>, less the items and trees a
/// rule hides by the provider's id).
/// </summary>
/// <remarks>
/// A tree is the rows whose key starts with the folder's key and a <c>/</c>. On SQLite, whose key column compares
/// bytes, that is the range <c>[key/, key0)</c>, which its planner seeks (a <c>LIKE</c> it can't: it ignores case
/// whatever the column's collation); on SQL Server, a <c>LIKE</c> with a fixed start (a seek too) and the start
/// compared again, as the library's folder term does.
/// </remarks>
internal sealed class RemoteSearchScope(bool caseInsensitive, bool sqlite)
{
    /// <summary>
    /// Rows of the tree of the folder at <paramref name="path"/> (connection path; empty: every row).
    /// </summary>
    public Expression<Func<RemoteFile, bool>> Below(string path) =>
        path.Length == 0 ? _ => true : BelowKey(RemoteIndexKeys.Key(path, caseInsensitive));

    /// <summary>Rows where <paramref name="visible"/> says the asker sees files.</summary>
    public Expression<Func<RemoteFile, bool>> Visible(PathFilter visible)
    {
        Expression<Func<RemoteFile, bool>>? any = null;
        foreach (var region in visible.Regions)
        {
            var inRegion = Below(region.Path);
            foreach (var except in region.Except)
                inRegion = And(inRegion, Not(Below(except)));
            any = any is null ? inRegion : Or(any, inRegion);
        }
        return any ?? (_ => false);
    }

    /// <summary>
    /// <paramref name="files"/> without the files whose provider id is one of <paramref name="hiddenIds"/>, nor those
    /// below a folder of the index with such an id.
    /// </summary>
    public async Task<IQueryable<RemoteFile>> WithoutHiddenAsync(
        SfeDbContext db,
        int connectionId,
        IQueryable<RemoteFile> files,
        IReadOnlySet<string> hiddenIds,
        CancellationToken cancellationToken
    )
    {
        if (hiddenIds.Count == 0)
            return files;
        var ids = hiddenIds.ToList();
        files = files.Where(f => f.ProviderId == null || !ids.Contains(f.ProviderId));
        var folders = await db
            .RemoteDirectories.AsNoTracking()
            .Where(d => d.ConnectionId == connectionId && d.ProviderId != null && ids.Contains(d.ProviderId))
            .Select(d => d.FullName)
            .ToListAsync(cancellationToken);
        foreach (var folder in folders.Where(f => f.Length > 0))
            files = files.Where(Not(BelowKey(folder)));
        return files;
    }

    private Expression<Func<RemoteFile, bool>> BelowKey(string key)
    {
        var prefix = key + "/";
        if (sqlite)
        {
            // '0' follows '/': every key starting with the prefix, and only those, sorts in between.
            var end = key + "0";
            return f => string.Compare(f.FullName, prefix) >= 0 && string.Compare(f.FullName, end) < 0;
        }
        var pattern = SearchPredicateBuilder.EscapeLike(prefix) + "%";
        var length = prefix.Length;
        return f =>
            EF.Functions.Like(f.FullName, pattern, SearchPredicateBuilder.LikeEscape)
            && f.FullName.Substring(0, length) == prefix;
    }

    private static Expression<Func<RemoteFile, bool>> Not(Expression<Func<RemoteFile, bool>> predicate) =>
        Expression.Lambda<Func<RemoteFile, bool>>(Expression.Not(predicate.Body), predicate.Parameters);

    private static Expression<Func<RemoteFile, bool>> And(
        Expression<Func<RemoteFile, bool>> left,
        Expression<Func<RemoteFile, bool>> right
    ) => Combine(left, right, Expression.AndAlso);

    private static Expression<Func<RemoteFile, bool>> Or(
        Expression<Func<RemoteFile, bool>> left,
        Expression<Func<RemoteFile, bool>> right
    ) => Combine(left, right, Expression.OrElse);

    private static Expression<Func<RemoteFile, bool>> Combine(
        Expression<Func<RemoteFile, bool>> left,
        Expression<Func<RemoteFile, bool>> right,
        Func<Expression, Expression, BinaryExpression> combine
    )
    {
        var parameter = left.Parameters[0];
        var body = new ParameterSwap(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<RemoteFile, bool>>(combine(left.Body, body), parameter);
    }

    private sealed class ParameterSwap(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
