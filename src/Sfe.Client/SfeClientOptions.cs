using System;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Where the server is, how to sign in, and how patient to be. Read once when the <see cref="SfeClient"/> is made:
/// changing them afterwards changes nothing for that client.
/// </summary>
public sealed class SfeClientOptions
{
    /// <summary>
    /// The default size of upload chunks: 8 MB, as the web page sends (the server takes up to 16 MB).
    /// </summary>
    internal const int DefaultChunkBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The server's address, with its virtual directory if it has one (<c>http://nas:5080/</c>,
    /// <c>https://example.org/sfe/</c>). May be left out when the <see cref="System.Net.Http.HttpClient"/> given to
    /// the client has a <c>BaseAddress</c>.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The personal access token (<c>sfe_…</c>), sent as <c>Authorization: Bearer</c>. Without one the client calls
    /// as a visitor, with what the server lets visitors do.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// The library (space) to work in, by its id (<see cref="SfeLibrary.Id"/>); null for the token's library, else
    /// the account's first.
    /// </summary>
    public int? Library { get; set; }

    /// <summary>
    /// How long one call may wait for the server's answer to start (its headers); a download's body may take longer.
    /// Default 100 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// How many times a call refused with 429 or 503 is tried again (reads and other calls that can safely be
    /// repeated only), and how many times an interrupted transfer goes on. Default 3; 0 never retries.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// The longest wait between two tries. A server asking (<c>Retry-After</c>) for longer gets no retry: the call
    /// fails at once with <see cref="SfeRateLimitedException.RetryAfter"/>. Default 60 seconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The wait before the first retry when the server doesn't say how long to wait (<c>Retry-After</c>); doubled
    /// at each following one. Default 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often a job or a count is asked for its progress while it is waited for. Default half a second.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The size of upload chunks, in bytes: what an upload holds in memory at most.</summary>
    internal int UploadChunkBytes { get; set; } = DefaultChunkBytes;

    /// <summary>A copy, so that the client keeps what it was given.</summary>
    internal SfeClientOptions Clone() => (SfeClientOptions)MemberwiseClone();
}
