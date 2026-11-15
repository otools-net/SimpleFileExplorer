using System;
using System.Net.Http;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>
/// One call of the facade: built again for each try, so that a retry sends the same body.
/// </summary>
internal sealed class SfeRequest
{
    public SfeRequest(HttpMethod method, string route)
    {
        Method = method;
        Route = route;
        CanRetry = method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Delete;
    }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The route, relative to the server's address (<see cref="Routes"/>).</summary>
    public string Route { get; }

    /// <summary>Makes the body of a try; null for none.</summary>
    public Func<HttpContent>? Content { get; init; }

    /// <summary>Headers of the call beyond the client's (a <c>Range</c>).</summary>
    public Action<HttpRequestMessage>? Headers { get; init; }

    /// <summary>
    /// Whether a refusal with 429 or 503 is tried again: reads and deletes by default; a <c>POST</c> or <c>PUT</c>
    /// only when sending it twice does no harm (a check, a chunk at an offset the server verifies).
    /// </summary>
    public bool CanRetry { get; init; }

    /// <summary>
    /// A status answered without throwing besides the successful ones (an upload's 409 at an offset).
    /// </summary>
    public int? AcceptedStatus { get; init; }

    /// <summary>A <c>GET</c> of <paramref name="route"/>.</summary>
    public static SfeRequest Get(string route) => new(HttpMethod.Get, route);

    /// <summary>A <c>POST</c> of <paramref name="body"/> as JSON (none when null); never retried.</summary>
    public static SfeRequest Post(string route, object? body = null) =>
        new(HttpMethod.Post, route) { Content = body is null ? null : () => SfeJson.Content(body) };

    /// <summary>A <c>DELETE</c> of <paramref name="route"/>.</summary>
    public static SfeRequest Delete(string route) => new(HttpMethod.Delete, route);
}
