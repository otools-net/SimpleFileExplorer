using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Archives;
using Otools.Net.Sfe.Client.Downloads;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Several files, or folders with everything in them, downloaded as one zip written while it is sent.
/// </summary>
public sealed class SfeArchives
{
    private readonly SfeTransport _transport;

    internal SfeArchives(SfeTransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// Writes a zip of <paramref name="paths"/> to <paramref name="destination"/> and answers the zip's name as the
    /// server gives it. Items the caller may not see are left out; links and what the caller may not download are
    /// listed in a text entry of the zip. A zip can't go on where it was cut off: it is asked again whole.
    /// </summary>
    /// <param name="paths">The files and folders (1 to the server's limit).</param>
    /// <param name="destination">Where the zip is written.</param>
    /// <param name="progress">Told the bytes written so far.</param>
    /// <param name="cancellationToken">Stops the download.</param>
    public async Task<string> DownloadZipAsync(
        IEnumerable<string> paths,
        Stream destination,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var request = new ArchiveRequest([.. paths.Select(SfePath.Normalize)]);
        var link = await _transport
            .ReadAsync<ArchiveLink>(SfeRequest.Post(Routes.V1("files/archive"), request), cancellationToken)
            .ConfigureAwait(false);
        // The link works once: a retry would find it used.
        var download = new SfeRequest(System.Net.Http.HttpMethod.Get, link.Url.TrimStart('/')) { CanRetry = false };
        using var response = await _transport.SendAsync(download, cancellationToken).ConfigureAwait(false);
        var cursor = new FileDownloads.Cursor(0, null);
        await FileDownloads.CopyAsync(response, destination, cursor, progress, cancellationToken).ConfigureAwait(false);
        return link.FileName;
    }
}
