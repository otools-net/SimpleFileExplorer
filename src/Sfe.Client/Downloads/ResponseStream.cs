using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Otools.Net.Sfe.Client.Downloads;

/// <summary>
/// The body of an answer, read as it comes; disposing it closes the answer (and frees the connection).
/// </summary>
internal sealed class ResponseStream(HttpResponseMessage response, Stream body) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    /// <summary>The length the server announced; unknown when it didn't (a zip written while sent).</summary>
    public override long Length => response.Content.Headers.ContentLength ?? throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => body.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        body.ReadAsync(buffer, offset, count, cancellationToken);

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            body.Dispose();
            response.Dispose();
        }
        base.Dispose(disposing);
    }
}
