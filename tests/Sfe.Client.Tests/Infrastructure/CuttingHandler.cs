namespace Otools.Net.Sfe.Client.Tests.Infrastructure;

/// <summary>
/// Cuts off the body of the first answer whose request matches, after <paramref name="bytes"/> bytes, as a dropped
/// connection would (an <see cref="IOException"/> while reading); the next answers pass whole.
/// </summary>
public sealed class CuttingHandler(HttpMessageHandler inner, Func<HttpRequestMessage, bool> matches, int bytes)
    : DelegatingHandler(inner)
{
    private int _cut;

    /// <summary>The requests that went through, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        lock (Requests)
            Requests.Add(request);
        var response = await base.SendAsync(request, cancellationToken);
        if (!matches(request) || Interlocked.Exchange(ref _cut, 1) == 1)
            return response;
        var whole = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var cutContent = new StreamContent(new CutStream(whole, bytes));
        foreach (var header in response.Content.Headers)
            cutContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        response.Content = cutContent;
        return response;
    }

    /// <summary>Gives the first bytes of an answer, then fails.</summary>
    private sealed class CutStream(byte[] whole, int bytes) : MemoryStream(whole, 0, Math.Min(bytes, whole.Length))
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, count) is > 0 and var read ? read : throw new IOException("Connection lost.");

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var array = new byte[buffer.Length];
            var read = Read(array, 0, array.Length);
            array.AsSpan(0, read).CopyTo(buffer.Span);
            return ValueTask.FromResult(read);
        }
    }
}
