using Otools.Net.Sfe.Core.Search;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>A file of a connection as a search reads it from the index (<see cref="RemoteIndexSearch"/>).</summary>
internal sealed record RemoteSearchRow(
    string FullName,
    string DisplayPath,
    string? ProviderId,
    string? NameKey,
    string Extension,
    long Size,
    DateTime LastWriteTime,
    int? Width,
    int? Height,
    double? Duration,
    long? Pixels,
    List<int> TagIds
) : ISearchRow
{
    string? ISearchRow.SortName => NameKey;
}
