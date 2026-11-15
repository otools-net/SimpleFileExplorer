using System.Linq.Expressions;

namespace Otools.Net.Sfe.Core.Search;

/// <summary>
/// What the search reads of a file of an index, by the same names on the library's files
/// (<see cref="Data.FileEntry"/>) and on a connection's (<see cref="Data.RemoteFile"/>): one predicate builder
/// (<see cref="SearchPredicateBuilder"/>) and one key paging (<see cref="SearchPages"/>) serve both. Every member is
/// a mapped column, so that EF Core translates it through the generic type.
/// </summary>
/// <typeparam name="TSelf">The entity.</typeparam>
public interface ISearchableFile<TSelf>
    where TSelf : class, ISearchableFile<TSelf>
{
    /// <summary>
    /// The key: the path compared and sorted by (the library's full path, a connection's lowered one).
    /// </summary>
    string FullName { get; }

    /// <summary>The name without its extension.</summary>
    string Name { get; }

    /// <summary>The extension with its dot; empty for a file without one.</summary>
    string Extension { get; }

    long Size { get; }

    DateTime LastWriteTime { get; }

    int? Width { get; }

    int? Height { get; }

    double? Duration { get; }

    long? Pixels { get; }

    /// <summary>The name as the name sort orders it: the name itself, or a stored key of natural order.</summary>
    static abstract Expression<Func<TSelf, string?>> SortName { get; }

    /// <summary>
    /// Some rows may have no <see cref="SortName"/> yet: the name sort lists them after the others, by
    /// <see cref="FullName"/>.
    /// </summary>
    static abstract bool SortNameMayBeMissing { get; }

    /// <summary>Files with at least one tag.</summary>
    static abstract Expression<Func<TSelf, bool>> IsTagged { get; }

    /// <summary>Files with at least one of the tags <paramref name="ids"/>.</summary>
    static abstract Expression<Func<TSelf, bool>> HasAnyTag(int[] ids);
}
