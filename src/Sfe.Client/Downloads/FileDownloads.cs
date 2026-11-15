using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client.Downloads;

/// <summary>
/// Files read from the server (<c>files/raw/{path}</c>): whole, from an offset, or a range, going on where a cut-off
/// answer stopped as long as the file hasn't changed (<c>If-Range</c> with its date).
/// </summary>
internal sealed class FileDownloads(SfeTransport transport)
{
    private const int BufferBytes = 81920;

    /// <summary>
    /// Writes the part of <paramref name="path"/> that <paramref name="options"/> names to
    /// <paramref name="destination"/>, and answers the bytes written.
    /// </summary>
    public async Task<long> DownloadAsync(
        string path,
        Stream destination,
        SfeDownloadOptions options,
        CancellationToken cancellationToken
    )
    {
        var route = Routes.WithPathSegments("files/raw", path);
        var cursor = new Cursor(options.Offset, options.Length);
        for (var failures = 0; ; failures++)
        {
            using var response = await OpenAsync(route, cursor, cancellationToken).ConfigureAwait(false);
            cursor.Version ??= response.Content.Headers.LastModified;
            try
            {
                await CopyAsync(response, destination, cursor, options.Progress, cancellationToken)
                    .ConfigureAwait(false);
                return cursor.Written;
            }
            catch (Exception cut) when (IsCutOff(cut, cancellationToken) && failures < transport.Options.MaxRetries)
            {
                var wait = RetryDelays.Backoff(failures, transport.Options.RetryDelay);
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>The content of <paramref name="path"/> from <paramref name="offset"/>, as it comes.</summary>
    public async Task<Stream> OpenReadAsync(string path, long offset, CancellationToken cancellationToken)
    {
        var route = Routes.WithPathSegments("files/raw", path);
        var response = await OpenAsync(route, new Cursor(offset, null), cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return new ResponseStream(response, body);
    }

    /// <summary>
    /// Copies an answer's body to <paramref name="destination"/>, counting in <paramref name="cursor"/>.
    /// </summary>
    public static async Task CopyAsync(
        HttpResponseMessage response,
        Stream destination,
        Cursor cursor,
        IProgress<long>? progress,
        CancellationToken cancellationToken
    )
    {
        using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var buffer = new byte[BufferBytes];
        int read;
        while ((read = await body.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            cursor.Written += read;
            progress?.Report(cursor.Written);
        }
    }

    /// <summary>
    /// The answer for what is left of <paramref name="cursor"/>'s part: a range from where it is; a later try only
    /// when the file is still the one of the first answer.
    /// </summary>
    private async Task<HttpResponseMessage> OpenAsync(string route, Cursor cursor, CancellationToken cancellationToken)
    {
        var request = new SfeRequest(HttpMethod.Get, route) { Headers = message => cursor.AddRange(message) };
        var response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (cursor.AsksPart && response.StatusCode != HttpStatusCode.PartialContent)
        {
            response.Dispose();
            throw new SfeException(
                cursor.Written > 0
                    ? "The file changed on the server while it was downloaded: download it again."
                    : "The server sent the whole file instead of the part asked for."
            );
        }
        return response;
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is the answer cut off on the way, not the caller stopping.
    /// </summary>
    private static bool IsCutOff(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception is IOException or HttpRequestException;

    /// <summary>Where a download stands: the part asked for, and how much of it is written.</summary>
    internal sealed class Cursor(long offset, long? length)
    {
        /// <summary>Bytes written by this download.</summary>
        public long Written { get; set; }

        /// <summary>The date of the file at the first answer, to go on only from the same file.</summary>
        public DateTimeOffset? Version { get; set; }

        /// <summary>The first byte still to fetch.</summary>
        public long Start => offset + Written;

        /// <summary>Whether a part is asked for rather than the whole file.</summary>
        public bool AsksPart => Start > 0 || length is not null;

        /// <summary>Adds the <c>Range</c> (and <c>If-Range</c>) of what is left, when not the whole file.</summary>
        public void AddRange(HttpRequestMessage message)
        {
            if (!AsksPart)
                return;
            long? last = length is { } count ? offset + count - 1 : null;
            message.Headers.Range = new RangeHeaderValue(Start, last);
            if (Version is { } version)
                message.Headers.IfRange = new RangeConditionHeaderValue(version);
        }
    }
}
