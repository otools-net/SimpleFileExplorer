using System;
using System.Net.Http;
using System.Threading;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Otools.Net.Sfe.Client.Generated;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// A program's way into an SFE server: files, jobs, trash, properties, zips, search and tags through the facade,
/// and every v1 route through <see cref="Raw"/>. Thread-safe; one <see cref="HttpClient"/> for all its calls.
/// </summary>
/// <example>
/// <code>
/// using var sfe = new SfeClient(new Uri("http://nas:5080"), token);
/// await foreach (var item in sfe.Files.ListAsync("Photos/2026"))
///     Console.WriteLine(item.Name);
/// </code>
/// </example>
public sealed class SfeClient : IDisposable
{
    /// <summary>The name of the <see cref="HttpClient"/> that <c>AddSfeClient</c> registers.</summary>
    public const string HttpClientName = "Sfe.Client";

    private readonly SfeTransport _transport;

    /// <summary>
    /// A client of the server at <paramref name="baseAddress"/>, signed in with <paramref name="token"/>.
    /// </summary>
    /// <param name="baseAddress">The server's address, with its virtual directory if any.</param>
    /// <param name="token">A personal access token (<c>sfe_…</c>).</param>
    public SfeClient(Uri baseAddress, string token)
        : this(new SfeClientOptions { BaseAddress = baseAddress, Token = token }) { }

    /// <summary>A client with its own <see cref="HttpClient"/>, disposed with it.</summary>
    /// <param name="options">The server's address, the token, the library, timeouts and retries.</param>
    public SfeClient(SfeClientOptions options)
        : this(CreateHttpClient(), ownsHttpClient: true, options) { }

    /// <summary>
    /// A client calling through <paramref name="httpClient"/> (from <c>IHttpClientFactory</c>, or with handlers of the
    /// program's), which it leaves open when disposed. The token and library go on each request, not on the client.
    /// </summary>
    /// <param name="httpClient">The client to call through; its <c>BaseAddress</c> serves when the options have
    /// none.</param>
    /// <param name="options">The token, the library, timeouts and retries.</param>
    public SfeClient(HttpClient httpClient, SfeClientOptions options)
        : this(httpClient, ownsHttpClient: false, options) { }

    private SfeClient(HttpClient httpClient, bool ownsHttpClient, SfeClientOptions options)
    {
        if (httpClient is null)
            throw new ArgumentNullException(nameof(httpClient));
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        var kept = options.Clone();
        _transport = new SfeTransport(httpClient, ownsHttpClient, kept);
        Server = new SfeServer(_transport);
        Libraries = new SfeLibraries(Server);
        Properties = new SfeProperties(_transport);
        Files = new SfeFiles(_transport, Properties);
        Trash = new SfeTrash(_transport);
        Archives = new SfeArchives(_transport);
        Search = new SfeSearch(_transport);
        Tags = new SfeTags(_transport);
        Raw = CreateRawClient(httpClient, kept, _transport.BaseAddress);
    }

    /// <summary>The server: its version, and the functions it offers.</summary>
    public SfeServer Server { get; }

    /// <summary>The libraries the caller reaches.</summary>
    public SfeLibraries Libraries { get; }

    /// <summary>Files and folders: list, read, upload, make, rename, move, copy, delete.</summary>
    public SfeFiles Files { get; }

    /// <summary>The library's trash.</summary>
    public SfeTrash Trash { get; }

    /// <summary>Properties of files and folders: dates, totals, counts, checksums.</summary>
    public SfeProperties Properties { get; }

    /// <summary>Zips of several files and folders.</summary>
    public SfeArchives Archives { get; }

    /// <summary>Searches of the library's index, in the app's search language.</summary>
    public SfeSearch Search { get; }

    /// <summary>Tags: listed, made, and given to or taken from files.</summary>
    public SfeTags Tags { get; }

    /// <summary>
    /// The client generated from the v1 document: every route, administration included, with the same token and
    /// library. Its calls have no retries, and its refusals are Kiota's <c>ApiException</c>.
    /// </summary>
    public SfeRawClient Raw { get; }

    /// <summary>Disposes the <see cref="HttpClient"/> when the client made it.</summary>
    public void Dispose() => _transport.Dispose();

    private static HttpClient CreateHttpClient() =>
        // The timeout is the options' (until headers); a download's body may take longer.
        new() { Timeout = Timeout.InfiniteTimeSpan };

    private static SfeRawClient CreateRawClient(HttpClient httpClient, SfeClientOptions options, Uri baseAddress)
    {
        var adapter = new HttpClientRequestAdapter(new SfeAuthenticationProvider(options), httpClient: httpClient)
        {
            // Kiota's routes start with "/": "http://nas/" would give "//api/v1/…".
            BaseUrl = baseAddress.AbsoluteUri.TrimEnd('/'),
        };
        return new SfeRawClient(adapter);
    }
}
