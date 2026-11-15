using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Otools.Net.Sfe.Client.Files;

/// <summary>
/// Paths sent in batches of what one request may name (deletes, moves inside a connection), the answers gathered in
/// the order of the paths.
/// </summary>
internal static class PathBatches
{
    /// <summary>Paths one request may name (the server's limit of deletes and moves).</summary>
    public const int Size = 1000;

    /// <summary>Sends <paramref name="paths"/>, normalized, a batch at a time; answers every batch's results.</summary>
    public static async Task<IReadOnlyList<T>> CollectAsync<T>(
        IEnumerable<string> paths,
        Func<IReadOnlyList<string>, Task<IEnumerable<T>>> send
    )
    {
        var results = new List<T>();
        var all = paths.Select(SfePath.Normalize).ToList();
        for (var first = 0; first < all.Count; first += Size)
            results.AddRange(await send([.. all.Skip(first).Take(Size)]).ConfigureAwait(false));
        return results;
    }
}
