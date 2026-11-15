using System.Linq.Expressions;
using System.Reflection;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The tag terms of a search of a connection's rows written from the link table's <c>TagId</c> index: each
/// <c>f.TagLinks.Any(l => …)</c> of the shared builder becomes <c>f.Id IN (SELECT FileId FROM RemoteFileTags WHERE
/// …)</c>, which starts from the tag's links; the <c>EXISTS</c> form walks the connection's rows (phase 0 of
/// agents/docs/development/connections-index-plan.md: a tag's count 2 s on SQLite over 2.4 M rows). Only the
/// connection's search goes through it: the library's search SQL is unchanged.
/// </summary>
internal sealed class RemoteTagTerms(SfeDbContext db) : ExpressionVisitor
{
    private static readonly MethodInfo AnyWithPredicate = typeof(Enumerable)
        .GetMethods()
        .Single(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(RemoteFileTag));

    private static readonly MethodInfo Contains = typeof(Queryable)
        .GetMethods()
        .Single(m => m.Name == nameof(Queryable.Contains) && m.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(long));

    /// <summary><paramref name="predicate"/> with its tag terms read from the links' index.</summary>
    public Expression<Func<RemoteFile, bool>> Rewrite(Expression<Func<RemoteFile, bool>> predicate) =>
        (Expression<Func<RemoteFile, bool>>)Visit(predicate);

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (
            node.Method != AnyWithPredicate
            || node.Arguments[0] is not MemberExpression { Member.Name: nameof(RemoteFile.TagLinks) } links
            || StripQuotes(node.Arguments[1]) is not Expression<Func<RemoteFileTag, bool>> condition
        )
        {
            return base.VisitMethodCall(node);
        }
        var fileIds = db.RemoteFileTags.Where(condition).Select(l => l.FileId);
        var id = Expression.Property(Visit(links.Expression)!, nameof(RemoteFile.Id));
        return Expression.Call(Contains, fileIds.Expression, id);
    }

    private static Expression StripQuotes(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;
}
