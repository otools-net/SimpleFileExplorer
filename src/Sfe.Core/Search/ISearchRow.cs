namespace Otools.Net.Sfe.Core.Search;

/// <summary>A search result as read from an index, with what the cursor of the next page is made of.</summary>
public interface ISearchRow
{
    /// <summary>The key (<see cref="ISearchableFile{TSelf}.FullName"/>).</summary>
    string FullName { get; }

    /// <summary>The name as the name sort orders it (<see cref="ISearchableFile{TSelf}.SortName"/>).</summary>
    string? SortName { get; }

    string Extension { get; }

    long Size { get; }

    DateTime LastWriteTime { get; }

    double? Duration { get; }

    long? Pixels { get; }
}
