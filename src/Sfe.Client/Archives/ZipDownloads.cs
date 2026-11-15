using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Downloads;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client.Archives;

/// <summary>
/// A zip of several items: its paths named (<c>POST</c> of the library's <c>files/archive</c> or a connection's
/// <c>archive</c>), then the one-time link the server answers read to the end.
/// </summary>
internal static class ZipDownloads
{
    /// <summary>Writes the zip of <paramref name="paths"/> to <paramref name="destination"/>; its name.</summary>
    /// <param name="transport">The client's calls.</param>
    /// <param name="route">The route naming the zip's paths.</param>
    /// <param name="paths">The files and folders.</param>
    /// <param name="destination">Where the zip is written, with progress.</param>
    /// <param name="cancellationToken">Stops the download.</param>
    public static async Task<string> DownloadAsync(
        SfeTransport transport,
        string route,
        IEnumerable<string> paths,
        (Stream Stream, IProgress<long>? Progress) destination,
        CancellationToken cancellationToken
    )
    {
        var request = new ArchiveRequest([.. paths.Select(SfePath.Normalize)]);
        var link = await transport
            .ReadAsync<ArchiveLink>(SfeRequest.Post(route, request), cancellationToken)
            .ConfigureAwait(false);
        // The link works once: a retry would find it used.
        var download = new SfeRequest(HttpMethod.Get, link.Url.TrimStart('/')) { CanRetry = false };
        using var response = await transport.SendAsync(download, cancellationToken).ConfigureAwait(false);
        var cursor = new FileDownloads.Cursor(0, null);
        await FileDownloads
            .CopyAsync(response, destination.Stream, cursor, destination.Progress, cancellationToken)
            .ConfigureAwait(false);
        return link.FileName;
    }
}
