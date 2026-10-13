using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Library;

namespace Sfe.Core.Search;

/// <summary>What makes an image or video high resolution (<c>r:hr</c>, <c>r:lr</c>, the HR/LR marks).</summary>
/// <param name="ImageExtensions">Extensions classified as images (without dot).</param>
/// <param name="ImagePixels">Smallest shorter side of a high-resolution image.</param>
public sealed record HighResolution(
    IReadOnlyCollection<string> ImageExtensions, int ImagePixels, IReadOnlyCollection<string> VideoExtensions, int VideoPixels);

/// <summary>The extensions (without dot) the listing classifies as each kind of media (<c>type:</c>).</summary>
public sealed record MediaKinds(IReadOnlyCollection<string> Images, IReadOnlyCollection<string> Videos, IReadOnlyCollection<string> Audio);

/// <summary>Translates a <see cref="SearchQuery"/> into an EF Core predicate over <see cref="FileEntry"/>.</summary>
public static class SearchPredicateBuilder
{
    public const string LikeEscape = "\\";

    /// <param name="resolveTag">
    /// Ids of the tags a tag term refers to, including the tags inheriting from them. Without it, tag terms match
    /// nothing.
    /// </param>
    /// <param name="highResolution">What <c>r:hr</c> and <c>r:lr</c> compare with; without it, they match nothing.</param>
    /// <param name="kinds">What <c>type:</c> compares with; without it, type terms match nothing.</param>
    public static Expression<Func<FileEntry, bool>> Build(
        SearchQuery query, Func<string, IReadOnlyCollection<int>>? resolveTag = null, HighResolution? highResolution = null, MediaKinds? kinds = null)
    {
        if (query.IsEmpty)
            return _ => true;

        var context = new Context(resolveTag ?? (_ => []), highResolution, kinds);
        return query.Groups
            .Select(group => BuildGroup(group, query.MatchFullPath, context))
            .Aggregate(Or);
    }

    private sealed record Context(Func<string, IReadOnlyCollection<int>> ResolveTag, HighResolution? HighResolution, MediaKinds? Kinds);

    /// <summary>Files with at least one of the tags.</summary>
    public static Expression<Func<FileEntry, bool>> HasAnyTag(IReadOnlyCollection<int> tagIds)
    {
        if (tagIds.Count == 0)
            return _ => false;
        var ids = tagIds.ToArray();
        return f => f.TagLinks.Any(l => ids.Contains(l.TagId));
    }

    private static Expression<Func<FileEntry, bool>> Tag(TagFilter filter, Func<string, IReadOnlyCollection<int>> resolveTag)
    {
        Expression<Func<FileEntry, bool>> hasTag = filter.Name is null
            ? f => f.TagLinks.Any()
            : HasAnyTag(resolveTag(filter.Name));
        return filter.Negated ? Negate(hasTag) : hasTag;
    }

    /// <summary>
    /// LIKE patterns matching a folder itself and everything below it. Drive roots ("V:\") keep their
    /// trailing separator, so it is only appended when missing.
    /// </summary>
    public static (string Self, string Descendants) TreePatterns(string directory)
    {
        var (self, prefix) = TreeBounds(directory);
        return (EscapeLike(self), EscapeLike(prefix) + "%");
    }

    /// <summary>
    /// Predicate matching rows whose path (selected by <paramref name="fullName"/>) is <paramref name="directory"/>
    /// or lies below it.
    /// </summary>
    /// <remarks>
    /// LIKE narrows the rows (and can use the index), but SQLite's LIKE ignores case whatever the column collation.
    /// The equality test that follows uses the column collation, which is case-sensitive on case-sensitive file
    /// systems, so "Movies" does not also match a sibling "movies" folder on Linux.
    /// </remarks>
    public static Expression<Func<T, bool>> InTree<T>(Expression<Func<T, string>> fullName, string directory)
    {
        var (self, prefix) = TreeBounds(directory);
        var (selfPattern, nestedPattern) = TreePatterns(directory);
        var length = prefix.Length;

        Expression<Func<string, bool>> test = name =>
            (EF.Functions.Like(name, selfPattern, LikeEscape) || EF.Functions.Like(name, nestedPattern, LikeEscape))
            && (name == self || name.Substring(0, length) == prefix);

        var body = new ParameterReplacer(test.Parameters[0], fullName.Body).Visit(test.Body);
        return Expression.Lambda<Func<T, bool>>(body, fullName.Parameters);
    }

    private static (string Self, string Prefix) TreeBounds(string directory)
    {
        var self = Path.TrimEndingDirectorySeparator(directory);
        var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        return (self, prefix);
    }

    /// <summary>Escapes LIKE wildcards so user input is matched literally.</summary>
    public static string EscapeLike(string value) => value
        .Replace(LikeEscape, LikeEscape + LikeEscape)
        .Replace("%", LikeEscape + "%")
        .Replace("_", LikeEscape + "_")
        .Replace("[", LikeEscape + "[");

    private static Expression<Func<FileEntry, bool>> BuildGroup(SearchGroup group, bool matchFullPath, Context context)
    {
        var conditions = new List<Expression<Func<FileEntry, bool>>>();

        conditions.AddRange(group.Include.Select(term => Contains(term, matchFullPath)));
        conditions.AddRange(group.Exclude.Select(term => Negate(Contains(term, matchFullPath))));
        conditions.AddRange(group.Sizes.Select(Size));
        conditions.AddRange(group.Dimensions.Select(Dimension));
        conditions.AddRange(group.Durations.Select(Duration));
        conditions.AddRange(group.Levels.Select(level => Level(level, context.HighResolution)));
        if (group.Extensions.Count > 0)
            conditions.Add(group.Extensions.Select(Extension).Aggregate(Or));
        if (group.Kinds.Count > 0)
            conditions.Add(group.Kinds.Select(kind => Kind(kind, context.Kinds)).Aggregate(Or));
        conditions.AddRange(group.Tags.Select(tag => Tag(tag, context.ResolveTag)));
        conditions.AddRange(group.ExcludedFilters.Select(excluded => Negate(BuildGroup(excluded, matchFullPath, context))));

        return conditions.Aggregate(And);
    }

    /// <summary>A video's or audio file's length compared to a number of seconds; 0 (not readable) and null (not read) never match.</summary>
    private static Expression<Func<FileEntry, bool>> Duration(DurationFilter filter)
    {
        var seconds = filter.Seconds;
        // Durations have fractions of a second: "=" is the same whole second, "about" anything rounding to the unit.
        var tolerance = filter.Comparison == SizeComparison.Equal ? 0.5 : filter.Tolerance;
        var min = seconds - tolerance;
        var max = seconds + tolerance;
        return filter.Comparison switch
        {
            SizeComparison.LessThan => f => f.Duration > 0 && f.Duration < seconds,
            SizeComparison.LessThanOrEqual => f => f.Duration > 0 && f.Duration <= seconds,
            SizeComparison.GreaterThan => f => f.Duration > 0 && f.Duration > seconds,
            SizeComparison.GreaterThanOrEqual => f => f.Duration > 0 && f.Duration >= seconds,
            SizeComparison.Equal => f => f.Duration > 0 && f.Duration >= min && f.Duration < max,
            _ => f => f.Duration > 0 && f.Duration >= min && f.Duration <= max,
        };
    }

    /// <summary>
    /// Images and videos whose shorter side reaches the threshold of their kind (both sides do), or known ones that
    /// don't; files whose resolution is not known are neither.
    /// </summary>
    private static Expression<Func<FileEntry, bool>> Level(ResolutionLevel level, HighResolution? thresholds)
    {
        if (thresholds is null)
            return _ => false;

        return new[]
            {
                (Extensions: thresholds.ImageExtensions, Pixels: thresholds.ImagePixels),
                (Extensions: thresholds.VideoExtensions, Pixels: thresholds.VideoPixels),
            }
            .Where(kind => kind.Extensions.Count > 0)
            .Select(kind =>
            {
                var pixels = kind.Pixels;
                Expression<Func<FileEntry, bool>> size = level == ResolutionLevel.High
                    ? f => f.Width >= pixels && f.Height >= pixels
                    : f => f.Width > 0 && f.Height > 0 && (f.Width < pixels || f.Height < pixels);
                return And(HasExtension(kind.Extensions), size);
            })
            .DefaultIfEmpty(f => false)
            .Aggregate(Or);
    }

    /// <summary>Files whose extension the listing classifies as <paramref name="kind"/>; "other" files have none of the media ones.</summary>
    private static Expression<Func<FileEntry, bool>> Kind(ItemKind kind, MediaKinds? kinds)
    {
        if (kinds is null)
            return _ => false;

        return kind switch
        {
            ItemKind.Image => HasExtension(kinds.Images),
            ItemKind.Video => HasExtension(kinds.Videos),
            ItemKind.Audio => HasExtension(kinds.Audio),
            _ => Negate(HasExtension([.. kinds.Images, .. kinds.Videos, .. kinds.Audio])),
        };
    }

    private static Expression<Func<FileEntry, bool>> Contains(string term, bool matchFullPath)
    {
        var pattern = "%" + EscapeLike(term) + "%";
        return matchFullPath
            ? f => EF.Functions.Like(f.FullName, pattern, LikeEscape)
            : f => EF.Functions.Like(f.Name, pattern, LikeEscape);
    }

    /// <summary>Files with one of <paramref name="extensions"/> (with or without the dot), compared like <c>e:</c>.</summary>
    internal static Expression<Func<FileEntry, bool>> HasExtension(IEnumerable<string> extensions) =>
        extensions.Select(e => Extension("." + e.TrimStart('.'))).DefaultIfEmpty(f => false).Aggregate(Or);

    private static Expression<Func<FileEntry, bool>> Extension(string extension)
    {
        var pattern = EscapeLike(extension);
        return f => EF.Functions.Like(f.Extension, pattern, LikeEscape);
    }

    private static Expression<Func<FileEntry, bool>> Size(SizeFilter filter)
    {
        var bytes = filter.Bytes;
        var min = filter.Bytes - filter.Tolerance;
        var max = filter.Bytes + filter.Tolerance;
        return filter.Comparison switch
        {
            SizeComparison.Equal => f => f.Size == bytes,
            SizeComparison.LessThan => f => f.Size < bytes,
            SizeComparison.LessThanOrEqual => f => f.Size <= bytes,
            SizeComparison.GreaterThan => f => f.Size > bytes,
            SizeComparison.GreaterThanOrEqual => f => f.Size >= bytes,
            _ => f => f.Size >= min && f.Size <= max,
        };
    }

    /// <summary>A side compared to a number of pixels; 0 (not readable) and null (not read) never match.</summary>
    private static Expression<Func<FileEntry, bool>> Dimension(DimensionFilter filter)
    {
        var pixels = filter.Pixels;
        return (filter.Dimension, filter.Comparison) switch
        {
            (Search.Dimension.Width, SizeComparison.LessThan) => f => f.Width > 0 && f.Width < pixels,
            (Search.Dimension.Width, SizeComparison.LessThanOrEqual) => f => f.Width > 0 && f.Width <= pixels,
            (Search.Dimension.Width, SizeComparison.GreaterThan) => f => f.Width > pixels,
            (Search.Dimension.Width, SizeComparison.GreaterThanOrEqual) => f => f.Width > 0 && f.Width >= pixels,
            (Search.Dimension.Width, _) => f => f.Width > 0 && f.Width == pixels,
            (_, SizeComparison.LessThan) => f => f.Height > 0 && f.Height < pixels,
            (_, SizeComparison.LessThanOrEqual) => f => f.Height > 0 && f.Height <= pixels,
            (_, SizeComparison.GreaterThan) => f => f.Height > pixels,
            (_, SizeComparison.GreaterThanOrEqual) => f => f.Height > 0 && f.Height >= pixels,
            _ => f => f.Height > 0 && f.Height == pixels,
        };
    }

    private static Expression<Func<FileEntry, bool>> Negate(Expression<Func<FileEntry, bool>> predicate) =>
        Expression.Lambda<Func<FileEntry, bool>>(Expression.Not(predicate.Body), predicate.Parameters);

    private static Expression<Func<FileEntry, bool>> And(Expression<Func<FileEntry, bool>> left, Expression<Func<FileEntry, bool>> right) =>
        Combine(left, right, Expression.AndAlso);

    private static Expression<Func<FileEntry, bool>> Or(Expression<Func<FileEntry, bool>> left, Expression<Func<FileEntry, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    /// <summary>Merges two lambdas into one sharing a single parameter (no Expression.Invoke, which EF Core cannot translate).</summary>
    private static Expression<Func<FileEntry, bool>> Combine(
        Expression<Func<FileEntry, bool>> left,
        Expression<Func<FileEntry, bool>> right,
        Func<Expression, Expression, BinaryExpression> combine)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<FileEntry, bool>>(combine(left.Body, rightBody), parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
