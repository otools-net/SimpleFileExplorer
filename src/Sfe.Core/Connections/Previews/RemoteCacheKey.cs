using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// The name of a remote file's version in the cache of its connection: hashes of the provider's id of the file and of
/// its version (the provider's content hash, else its size and date). Made from what the provider (or the library's
/// index of it) says of the file, never from a request: nobody names a cache entry. A file changed at the provider has
/// another version, so another entry: it is fetched again.
/// </summary>
/// <param name="File">The file's part (its id): the versions of one file share it.</param>
/// <param name="Version">The version's part.</param>
public sealed record RemoteCacheKey(string File, string Version)
{
    /// <summary>Hex characters kept of each hash (80 bits: no two files meet in practice).</summary>
    private const int PartLength = 20;

    /// <summary>The entry's name: <c>{file}-{version}</c>.</summary>
    public string Name => $"{File}-{Version}";

    /// <summary>The key of a file as the provider gives it.</summary>
    /// <param name="caseInsensitive">The provider ignores the case of names: a file without an id is known by its
    /// path in lower case.</param>
    public static RemoteCacheKey Of(RemoteItem item, bool caseInsensitive) =>
        Of(item.Id, item.Path, caseInsensitive, (item.Hash, item.Size, item.Modified));

    /// <summary>The key of a file from its id or path and what tells its version.</summary>
    public static RemoteCacheKey Of(
        string? providerId,
        string path,
        bool caseInsensitive,
        (string? Hash, long? Size, DateTime? Modified) version
    )
    {
        var file = providerId is { Length: > 0 } id
            ? "id:" + id
            : "path:" + (caseInsensitive ? path.ToLowerInvariant() : path);
        var ticks = (version.Modified ?? default).Ticks.ToString(CultureInfo.InvariantCulture);
        var told = version.Hash is { Length: > 0 } hash ? "hash:" + hash : $"size:{version.Size ?? 0}:{ticks}";
        return new RemoteCacheKey(HashOf(file), HashOf(told));
    }

    private static string HashOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..PartLength];
}
