using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// How a connection's paths become the index's columns: the key (<see cref="RemoteFile.FullName"/>, lowered when the
/// provider ignores case), the name and extension, and what the index can't hold (paths longer than
/// <see cref="SfeDbContext.MaxPathLength"/>, ids and hashes longer than their columns), left out rather than cut.
/// </summary>
public static class RemoteIndexKeys
{
    /// <summary>The start of the key of a row set aside (<see cref="Parked"/>): never a path's.</summary>
    public const string ParkedPrefix = "\u0001removed/";

    /// <summary>The key of a path: as the provider compares names (lowered when it ignores case).</summary>
    public static string Key(string path, bool caseInsensitive) => caseInsensitive ? path.ToLowerInvariant() : path;

    /// <summary>The key of a row that left its path while its file may still be met elsewhere (by its id).</summary>
    public static string Parked(long id) =>
        ParkedPrefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Longer than the index stores: such an item is told about and left out, never cut.</summary>
    public static bool IsTooLong(string path) => path.Length > SfeDbContext.MaxPathLength;

    /// <summary>The item's provider id, when the index can keep it.</summary>
    public static string? IdOf(RemoteItem item) =>
        item.Id is { Length: > 0 and <= RemoteIndexModel.MaxProviderIdLength } id ? id : null;

    /// <summary>The item's content hash, when the index can keep it.</summary>
    public static string? HashOf(RemoteItem item) =>
        item.Hash is { Length: > 0 and <= RemoteIndexModel.MaxHashLength } hash ? hash : null;

    /// <summary>A file's name without its extension, and its extension with the dot (as the library's index).</summary>
    public static (string Name, string Extension) Split(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return (fileName[..^extension.Length], extension);
    }

    /// <summary>Fills a file's row from the provider's item (path and folder aside).</summary>
    public static void Describe(RemoteFile row, RemoteItem item)
    {
        var (name, extension) = Split(RemotePaths.NameOf(item.Path));
        row.Name = name;
        row.Extension = extension;
        row.DisplayPath = item.Path;
        row.Size = item.Size ?? 0;
        row.LastWriteTime = item.Modified is { } modified ? DateTime.SpecifyKind(modified, DateTimeKind.Utc) : default;
        row.ContentHash = HashOf(item);
        row.Downloadable = item.Downloadable;
        row.ProviderId = IdOf(item) ?? row.ProviderId;
    }
}
