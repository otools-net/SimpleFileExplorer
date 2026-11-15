using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client.Uploads;

/// <summary>
/// Sends an upload's bytes in chunks (<c>PUT files/uploads/{id}?offset=</c> of the library or a connection), one
/// buffer in memory whatever the size (each chunk a byte array, so that no target streams a request body of unknown
/// length). A chunk cut off on the way is sent again from what the server has; a chunk refused as too large (413, a
/// proxy's limit) is halved, down to 256 KB, as the web page does.
/// </summary>
internal sealed class UploadChunks(SfeTransport transport, FilePlace place, UploadInfo upload, int chunkBytes)
{
    private const int SmallestChunk = 256 * 1024;

    private int _chunkBytes = chunkBytes;
    private int _failures;

    /// <summary>
    /// Sends <paramref name="content"/> from what the server has (<see cref="UploadInfo.Received"/>; the stream is at
    /// that byte) to the upload's size. <paramref name="origin"/> is the stream's position of byte 0.
    /// </summary>
    public async Task SendAsync(
        Stream content,
        long origin,
        IProgress<long>? progress,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[(int)Math.Max(1, Math.Min(_chunkBytes, upload.Size))];
        var sent = upload.Received;
        progress?.Report(sent);
        while (sent < upload.Size)
        {
            var count = await FillAsync(
                    content,
                    buffer,
                    (int)Math.Min(buffer.Length, upload.Size - sent),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (count == 0)
                throw new EndOfStreamException("The content ended before the size given for the upload.");
            var received = await SendBufferAsync(buffer, count, sent, progress, cancellationToken)
                .ConfigureAwait(false);
            if (received != sent + count)
                MoveTo(content, origin, received);
            sent = received;
        }
    }

    /// <summary>
    /// Sends <paramref name="count"/> bytes of <paramref name="buffer"/>, which start at byte
    /// <paramref name="start"/>, in chunks; answers where the server stands after them (outside the buffer when the
    /// server has another idea of the upload).
    /// </summary>
    private async Task<long> SendBufferAsync(
        byte[] buffer,
        int count,
        long start,
        IProgress<long>? progress,
        CancellationToken cancellationToken
    )
    {
        var done = 0;
        while (done < count)
        {
            var piece = Math.Min(_chunkBytes, count - done);
            var received = await PutAsync(start + done, buffer, done, piece, cancellationToken).ConfigureAwait(false);
            if (received < start || received > start + count)
                return received;
            done = (int)(received - start);
            progress?.Report(received);
        }
        return start + count;
    }

    /// <summary>
    /// Sends one chunk at <paramref name="offset"/> and answers what the server has now: past the chunk, or where it
    /// asks to go on from (a lost reply, a cut-off), or <paramref name="offset"/> again with a smaller chunk to come.
    /// </summary>
    private async Task<long> PutAsync(
        long offset,
        byte[] buffer,
        int index,
        int count,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var info = await SendChunkAsync(offset, buffer, index, count, cancellationToken).ConfigureAwait(false);
            _failures = 0;
            return info.Received;
        }
        catch (SfeApiException tooLarge) when (tooLarge.StatusCode == 413 && _chunkBytes > SmallestChunk)
        {
            _chunkBytes = Math.Max(SmallestChunk, _chunkBytes / 2);
            return offset;
        }
        catch (Exception cut) when (IsCutOff(cut, cancellationToken) && ++_failures <= transport.Options.MaxRetries)
        {
            var wait = RetryDelays.Backoff(_failures - 1, transport.Options.RetryDelay);
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            return await ReceivedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The chunk's <c>PUT</c>; a 409 answers the upload as the server has it, to go on from there.</summary>
    private async Task<UploadInfo> SendChunkAsync(
        long offset,
        byte[] buffer,
        int index,
        int count,
        CancellationToken cancellationToken
    )
    {
        var route = place.Route($"files/uploads/{upload.Id}?offset={offset.ToString(CultureInfo.InvariantCulture)}");
        var request = new SfeRequest(HttpMethod.Put, route)
        {
            Content = () => new ByteArrayContent(buffer, index, count),
            // The server checks the offset: a chunk sent twice is refused (409), never written twice.
            CanRetry = true,
            AcceptedStatus = 409,
        };
        return await transport.ReadAsync<UploadInfo>(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> ReceivedAsync(CancellationToken cancellationToken)
    {
        var route = place.Route($"files/uploads/{upload.Id}");
        var info = await transport
            .ReadAsync<UploadInfo>(SfeRequest.Get(route), cancellationToken)
            .ConfigureAwait(false);
        return info.Received;
    }

    /// <summary>Puts the stream at byte <paramref name="received"/>, where the server asks to go on from.</summary>
    private static void MoveTo(Stream content, long origin, long received)
    {
        if (!content.CanSeek)
            throw new SfeException("The server lost part of the upload, and the content can't be read again.");
        content.Position = origin + received;
    }

    /// <summary>Reads up to <paramref name="count"/> bytes: fewer only at the end of the stream.</summary>
    private static async Task<int> FillAsync(
        Stream content,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken
    )
    {
        var filled = 0;
        while (filled < count)
        {
            var read = await content.ReadAsync(buffer, filled, count - filled, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            filled += read;
        }
        return filled;
    }

    private static bool IsCutOff(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is IOException or HttpRequestException or TimeoutException;
}
