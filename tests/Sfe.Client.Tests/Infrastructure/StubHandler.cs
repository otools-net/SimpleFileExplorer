using System.Net;
using System.Text;

namespace Otools.Net.Sfe.Client.Tests.Infrastructure;

/// <summary>
/// A server that isn't one: answers the requests in turn from the list given (the last one again once they run out),
/// and keeps the requests, to test what the library does with statuses the real server rarely gives.
/// </summary>
public sealed class StubHandler(params Func<HttpResponseMessage>[] answers) : HttpMessageHandler
{
    private int _calls;

    /// <summary>The requests received, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>An answer of <paramref name="status"/> with a JSON <paramref name="body"/> (a problem, say).</summary>
    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A 429 asking to wait <paramref name="seconds"/>.</summary>
    public static HttpResponseMessage TooMany(int seconds)
    {
        var answer = Json(HttpStatusCode.TooManyRequests, """{"title":"Too many requests"}""");
        answer.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(seconds)
        );
        return answer;
    }

    /// <summary>A client of this stub, with retries that wait no more than a moment.</summary>
    public SfeClient CreateClient(Action<SfeClientOptions>? configure = null)
    {
        var options = new SfeClientOptions
        {
            BaseAddress = new Uri("http://stub/"),
            Token = "sfe_test",
            RetryDelay = TimeSpan.FromMilliseconds(1),
        };
        configure?.Invoke(options);
        return new SfeClient(new HttpClient(this), options);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        var index = Math.Min(Interlocked.Increment(ref _calls) - 1, answers.Length - 1);
        return Task.FromResult(answers[index]());
    }
}
