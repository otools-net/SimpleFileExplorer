namespace Sfe.Core.Connections;

/// <summary>
/// Bytes read from a remote storage: the whole file, or the part served when a range was asked for and the provider
/// could. Disposing it ends the read at the provider.
/// </summary>
/// <param name="Length">Bytes in <paramref name="Stream"/>; null when the provider does not say.</param>
/// <param name="TotalLength">The size of the whole file; null when unknown.</param>
/// <param name="Range">What is served, when it is a part; null for the whole file.</param>
public sealed record RemoteContent(
    Stream Stream,
    long? Length,
    long? TotalLength = null,
    RemoteRange? Range = null,
    string? ContentType = null
) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
