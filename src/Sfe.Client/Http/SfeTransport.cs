using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>
/// Sends the facade's calls on the client's one <see cref="HttpClient"/>: the token and library headers on each
/// request (the client itself is left untouched, so that one from a factory can be shared), a timeout until the
/// answer's headers, retries of 429 and 503, and refusals turned into <see cref="SfeApiException"/>s.
/// </summary>
internal sealed class SfeTransport : IDisposable
{
    private const string TenantHeader = "X-SFE-Tenant";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public SfeTransport(HttpClient http, bool ownsHttp, SfeClientOptions options)
    {
        _http = http;
        _ownsHttp = ownsHttp;
        Options = options;
        var address = options.BaseAddress ?? http.BaseAddress;
        if (address is null || !address.IsAbsoluteUri)
            throw new ArgumentException("The server's address (BaseAddress) is needed, as an absolute address.");
        BaseAddress = address.AbsoluteUri.EndsWith("/") ? address : new Uri(address.AbsoluteUri + "/");
    }

    /// <summary>The server's address, ending with <c>/</c> so that routes resolve below it.</summary>
    public Uri BaseAddress { get; }

    /// <summary>The client's options, a copy of those given.</summary>
    public SfeClientOptions Options { get; }

    /// <summary>The <see cref="HttpClient"/> the calls go through.</summary>
    public HttpClient Http => _http;

    /// <summary>
    /// Sends <paramref name="request"/> and answers once its headers are read: a successful answer, or the accepted
    /// status; any other is thrown as an <see cref="SfeApiException"/> after the retries it may have. The caller
    /// disposes the answer.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(SfeRequest request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode || status == request.AcceptedStatus)
                return response;

            var retryAfter = RetryDelays.RetryAfter(response);
            var problem = await ReadProblemAsync(response).ConfigureAwait(false);
            var wait = request.CanRetry ? RetryDelays.Next(status, problem, retryAfter, attempt, Options) : null;
            if (wait is null)
                throw ProblemReader.ToException(status, problem, retryAfter);
            await Task.Delay(wait.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sends <paramref name="request"/> and reads its answer as <typeparamref name="T"/>.</summary>
    public async Task<T> ReadAsync<T>(SfeRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await SfeJson.ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends <paramref name="request"/>, whose answer has nothing to read.</summary>
    public async Task RunAsync(SfeRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The address of <paramref name="route"/> (relative to the server's address).</summary>
    public Uri Resolve(string route) => new(BaseAddress, route);

    /// <summary>
    /// One try, cut off with a <see cref="TimeoutException"/> after <see cref="SfeClientOptions.RequestTimeout"/>
    /// without headers.
    /// </summary>
    private async Task<HttpResponseMessage> SendOnceAsync(SfeRequest request, CancellationToken cancellationToken)
    {
        using var message = Build(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Options.RequestTimeout);
        try
        {
            return await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The server didn't answer within {Options.RequestTimeout}.");
        }
    }

    private HttpRequestMessage Build(SfeRequest request)
    {
        var message = new HttpRequestMessage(request.Method, Resolve(request.Route));
        if (Options.Token is { Length: > 0 } token)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (Options.Library is { } library)
            message.Headers.Add(TenantHeader, library.ToString(System.Globalization.CultureInfo.InvariantCulture));
        message.Content = request.Content?.Invoke();
        request.Headers?.Invoke(message);
        return message;
    }

    private static async Task<SfeProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        using (response)
            return await ProblemReader.ReadAsync(response).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
