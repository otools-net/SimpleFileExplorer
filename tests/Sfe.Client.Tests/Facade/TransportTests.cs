using System.Net;
using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// What every call of the facade does with the server's answers: refusals read into exceptions, retries of 429 and
/// 503, timeouts and cancellation. The statuses the real server gives seldom come from a stub.
/// </summary>
[TestClass]
public sealed class TransportTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private const string ConfigBody = """{"version":"9.9.9","mode":"external","rights":[],"tenants":[]}""";

    [TestMethod]
    public async Task ARead429IsTriedAgainAfterTheWaitAsked()
    {
        var stub = new StubHandler(() => StubHandler.TooMany(0), () => StubHandler.Json(HttpStatusCode.OK, ConfigBody));
        using var sfe = stub.CreateClient();

        var info = await sfe.Server.GetInfoAsync(Token);

        Assert.AreEqual("9.9.9", info.Version);
        Assert.HasCount(2, stub.Requests);
        Assert.AreEqual("Bearer sfe_test", stub.Requests[0].Headers.Authorization?.ToString());
        Assert.IsFalse(stub.Requests[0].Headers.Contains("X-SFE-Request"));
    }

    [TestMethod]
    public async Task APostIsNeverTriedAgain()
    {
        var stub = new StubHandler(() => StubHandler.TooMany(0));
        using var sfe = stub.CreateClient();

        var refused = await Assert.ThrowsExactlyAsync<SfeRateLimitedException>(() =>
            sfe.Files.CreateFolderAsync("New", Token)
        );

        Assert.HasCount(1, stub.Requests);
        Assert.AreEqual(TimeSpan.Zero, refused.RetryAfter);
    }

    [TestMethod]
    public async Task AWaitLongerThanAllowedFailsAtOnce()
    {
        var stub = new StubHandler(() => StubHandler.TooMany(3600));
        using var sfe = stub.CreateClient();

        var refused = await Assert.ThrowsExactlyAsync<SfeRateLimitedException>(() => sfe.Server.GetInfoAsync(Token));

        Assert.HasCount(1, stub.Requests);
        Assert.AreEqual(TimeSpan.FromHours(1), refused.RetryAfter);
    }

    [TestMethod]
    public async Task RetriesStopAfterTheNumberAllowed()
    {
        var stub = new StubHandler(() => StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));
        using var sfe = stub.CreateClient(options => options.MaxRetries = 2);

        var refused = await Assert.ThrowsExactlyAsync<SfeApiException>(() => sfe.Server.GetInfoAsync(Token));

        Assert.AreEqual(503, refused.StatusCode);
        Assert.HasCount(3, stub.Requests);
    }

    [TestMethod]
    public async Task AServerWaitingForItsUpdateIsNotTriedAgain()
    {
        var problem = """{"type":"migration-required","title":"Update needed","status":503}""";
        var stub = new StubHandler(() => StubHandler.Json(HttpStatusCode.ServiceUnavailable, problem));
        using var sfe = stub.CreateClient();

        var refused = await Assert.ThrowsExactlyAsync<SfeServerNotReadyException>(() => sfe.Server.GetInfoAsync(Token));

        Assert.AreEqual("migration-required", refused.ProblemType);
        Assert.HasCount(1, stub.Requests);
    }

    [TestMethod]
    public async Task AProblemWithoutAWordOfItsOwnKeepsItsStatusAndDetail()
    {
        var problem = """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
              "detail": "Bad name",
              "errors": { "name": ["Too long"] }
            }
            """;
        var stub = new StubHandler(() => StubHandler.Json(HttpStatusCode.BadRequest, problem));
        using var sfe = stub.CreateClient();

        var refused = await Assert.ThrowsExactlyAsync<SfeApiException>(() => sfe.Files.CreateFolderAsync("x", Token));

        Assert.IsNull(refused.ProblemType);
        Assert.AreEqual("Bad name", refused.Detail);
        CollectionAssert.AreEqual(new[] { "Too long" }, refused.Errors["name"]);
    }

    [TestMethod]
    public async Task AnAnswerSlowerThanTheTimeoutFailsWithTimeoutException()
    {
        var stub = new SlowHandler();
        using var sfe = new SfeClient(
            new HttpClient(stub),
            new SfeClientOptions
            {
                BaseAddress = new Uri("http://stub/"),
                RequestTimeout = TimeSpan.FromMilliseconds(50),
            }
        );

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => sfe.Server.GetInfoAsync(Token));
    }

    [TestMethod]
    public async Task ACancelledCallStopsWithoutTimeoutException()
    {
        using var sfe = new SfeClient(
            new HttpClient(new SlowHandler()),
            new SfeClientOptions { BaseAddress = new Uri("http://stub/") }
        );
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancel.CancelAfter(50);

        await Assert.ThrowsAsync<OperationCanceledException>(() => sfe.Server.GetInfoAsync(cancel.Token));
    }

    [TestMethod]
    public async Task AWordOfANewerServerReadsAsUnknown()
    {
        var body = """{"version":"9.9.9","mode":"cloud","rights":[],"tenants":[],"newProperty":{"a":1}}""";
        var stub = new StubHandler(() => StubHandler.Json(HttpStatusCode.OK, body));
        using var sfe = stub.CreateClient();

        var info = await sfe.Server.GetInfoAsync(Token);

        Assert.AreEqual(SfeServerMode.Unknown, info.Mode);
    }

    /// <summary>A server that never answers.</summary>
    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
