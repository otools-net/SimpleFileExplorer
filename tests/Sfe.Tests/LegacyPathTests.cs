using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sfe.Api;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The unversioned paths (<c>/api/…</c>) are another name of v1 until SFE 2.0 (<see cref="LegacyApiPaths"/>): every
/// route answers at its old path as at its v1 path, says the old one is deprecated, and the log hears of each caller
/// once a day.
/// </summary>
[TestClass]
public sealed partial class LegacyPathTests
{
    private const string V1 = "api/v1/";

    // For the routes taking a path in the query; unknown query parameters are ignored by the others.
    private const string SamplePath = "ChildDir/AFile1.txt";

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        _library.Dispose();
    }

    /// <summary>
    /// Anonymous, every method of every route: refused or answered alike, before anything changes. Signed in, the
    /// routes that only read (GET), which can run twice: a route that changes something would answer its second call
    /// differently whatever its path (created, then a conflict), and reaches its handler only through the same
    /// endpoint the anonymous calls already proved.
    /// </summary>
    [TestMethod]
    public async Task EveryRouteAnswersAtItsOldPathAsAtV1()
    {
        using var visitor = ApiClients.Create(_factory);
        var routes = Routes();
        var differences = new List<string>();

        foreach (var (method, path) in routes)
            await CompareAsync(visitor, method, path, differences);
        foreach (var (method, path) in routes.Where(route => route.Method == HttpMethods.Get))
            await CompareAsync(_admin, method, path, differences);

        Assert.IsGreaterThan(200, routes.Count);
        Assert.IsEmpty(differences, string.Join(Environment.NewLine, differences));
    }

    [TestMethod]
    public async Task OldPathsSayTheyAreDeprecatedWithALinkToV1()
    {
        using var old = await _admin.GetAsync("/api/tags", Token);
        using var current = await _admin.GetAsync("/api/v1/tags", Token);

        Assert.AreEqual(HttpStatusCode.OK, old.StatusCode);
        Assert.AreEqual(LegacyApiPaths.DeprecatedSince, string.Join(',', old.Headers.GetValues("Deprecation")));
        Assert.AreEqual("</api/v1/tags>; rel=\"successor-version\"", string.Join(',', old.Headers.GetValues("Link")));
        Assert.IsFalse(current.Headers.Contains("Deprecation"));
        Assert.IsFalse(current.Headers.Contains("Link"));
    }

    [TestMethod]
    public async Task UnknownPathsAreNotFoundUnderBothNames()
    {
        using var old = await _admin.GetAsync("/api/no-such-route", Token);
        using var current = await _admin.GetAsync("/api/v1/no-such-route", Token);
        using var later = await _admin.GetAsync("/api/v2/tags", Token);

        Assert.AreEqual(HttpStatusCode.NotFound, old.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, current.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, later.StatusCode, "Another version is not an alias of v1");
        Assert.IsFalse(later.Headers.Contains("Deprecation"));
    }

    [TestMethod]
    public async Task TheDocumentationAndTheOpenApiDocumentStayWhereTheyWere()
    {
        using var docs = await _admin.GetAsync("/api/docs/index.html", Token);
        using var document = await _admin.GetAsync("/openapi/v1.json", Token);
        using var json = JsonDocument.Parse(await document.Content.ReadAsStringAsync(Token));
        var paths = json.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        Assert.AreEqual(HttpStatusCode.OK, docs.StatusCode);
        Assert.IsFalse(docs.Headers.Contains("Deprecation"));
        Assert.IsGreaterThan(100, paths.Count);
        Assert.IsEmpty(
            paths.Where(path => !path.StartsWith("/" + V1, StringComparison.Ordinal)).ToList(),
            "The document shows the v1 paths only"
        );
    }

    /// <summary>
    /// Administrators registered the return address at the provider before v1: it stays the unversioned path, answered
    /// without notice of an end.
    /// </summary>
    [TestMethod]
    public async Task TheOAuthReturnAddressKeepsItsUnversionedPath()
    {
        using var callback = await _admin.GetAsync($"{ApiRoutes.OAuthCallback}?state=x&code=y", Token);
        var setup = await _admin.GetFromJsonAsync<ProviderSetupView>("/api/v1/connections/providers/dropbox", Token);

        Assert.AreEqual(ApiRoutes.LegacyBase + ConnectionProviderEndpoints.CallbackPath, ApiRoutes.OAuthCallback);
        Assert.AreEqual(
            $"http://localhost{ApiRoutes.OAuthCallback}",
            setup!.ReturnAddress,
            "The address to register at the provider"
        );
        Assert.AreNotEqual(HttpStatusCode.NotFound, callback.StatusCode);
        Assert.IsFalse(callback.Headers.Contains("Deprecation"));
    }

    /// <summary>External player links handed out before the upgrade (valid for days) carry the old path.</summary>
    [TestMethod]
    public async Task LinksHandedOutBeforeV1StillOpen()
    {
        using var created = await _admin.PostAsJsonAsync("/api/v1/files/links", new FileLinkRequest(SamplePath), Token);
        var link = (await created.Content.ReadFromJsonAsync<FileLink>(Token))!;
        Assert.StartsWith(V1, link.Url);
        using var visitor = ApiClients.Create(_factory);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/" + link.Url[V1.Length..]);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 5);
        using var response = await visitor.SendAsync(request, Token);

        Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.HasCount(4, await response.Content.ReadAsByteArrayAsync(Token));
    }

    [TestMethod]
    public async Task EachCallerOfAnOldPathIsLoggedOnceADay()
    {
        var log = new WarningLog();
        await using var logged = _factory.WithWebHostBuilder(builder => builder.ConfigureLogging(log.AddTo));
        using var admin = await ApiClients.SignInAsync(logged, ApiClients.AdminName, ApiClients.AdminPassword, Token);
        using var visitor = ApiClients.CreateFrom(logged, "10.1.2.3");

        for (var i = 0; i < 3; i++)
            using (await admin.GetAsync("/api/tags", Token)) { }
        using (await admin.GetAsync("/api/v1/tags", Token)) { }
        using (await visitor.GetAsync("/api/session", Token)) { }
        using (await visitor.GetAsync("/api/session", Token)) { }

        var lines = log.Entries.Where(line => line.Contains(nameof(LegacyPathLog), StringComparison.Ordinal)).ToList();
        // The sign-in itself came through an old path: the account's line of the day is already written then.
        Assert.HasCount(
            1,
            lines.Where(line => line.Contains("Account ", StringComparison.Ordinal)).ToList(),
            log.ToString()
        );
        Assert.HasCount(1, lines.Where(line => line.Contains("Address 10.1.2.3", StringComparison.Ordinal)).ToList());
    }

    [TestMethod]
    public void TheLogForgetsYesterdayAndStopsAtItsLimit()
    {
        var log = new WarningLog();
        var time = new SettableTime();
        using var loggers = LoggerFactory.Create(log.AddTo);
        var legacy = new LegacyPathLog(time, loggers.CreateLogger<LegacyPathLog>());

        legacy.Note(Caller("10.0.0.1"), "/api/tags");
        legacy.Note(Caller("10.0.0.1"), "/api/files/shared/secret-token/name.mp4");
        Assert.HasCount(1, log.Entries);
        time.Now += TimeSpan.FromDays(1);
        legacy.Note(Caller("10.0.0.1"), "/api/files/shared/secret-token/name.mp4");
        Assert.HasCount(2, log.Entries);
        Assert.Contains("/api/files/shared…", log.Entries.Last());
        Assert.DoesNotContain("secret-token", log.Entries.Last(), "Links' tokens stay out of the log");

        for (var i = 0; i < LegacyPathLog.MaxCallersPerDay + 10; i++)
            legacy.Note(Caller($"10.1.{i / 256}.{i % 256}"), "/api/tags");
        Assert.HasCount(1 + LegacyPathLog.MaxCallersPerDay + 1, log.Entries, "One line per caller, then one more");
    }

    private static DefaultHttpContext Caller(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        context.Request.Method = HttpMethods.Get;
        return context;
    }

    private async Task CompareAsync(HttpClient client, string method, string v1Path, List<string> differences)
    {
        var oldPath = "/api/" + v1Path[V1.Length..];
        var (oldStatus, deprecated) = await StatusAsync(client, method, oldPath);
        var (newStatus, _) = await StatusAsync(client, method, "/" + v1Path);
        if (oldStatus != newStatus)
            differences.Add($"{method} {oldPath}: {oldStatus}, at v1 {newStatus}");
        else if (!deprecated && !oldPath.StartsWith(ApiRoutes.OAuthCallback, StringComparison.Ordinal))
            differences.Add($"{method} {oldPath}: no Deprecation header");
    }

    // Headers only: the change feed streams for as long as it is open.
    private async Task<(string Status, bool Deprecated)> StatusAsync(HttpClient client, string method, string path)
    {
        var separator = path.Contains('?') ? '&' : '?';
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{path}{separator}path={SamplePath}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );
            // A redirect was followed (fast sign-in): the last answer is the page's, not the route's.
            var redirected = response.RequestMessage?.RequestUri?.AbsolutePath != path;
            return (((int)response.StatusCode).ToString(), redirected || response.Headers.Contains("Deprecation"));
        }
        catch (OperationCanceledException) when (!Token.IsCancellationRequested)
        {
            return ("no answer in 10 s", true);
        }
    }

    /// <summary>Every v1 route with each of its methods, its parameters filled with sample values.</summary>
    private List<(string Method, string Path)> Routes() =>
        _factory
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<StaticAssetDescriptor>() is null)
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith(V1, StringComparison.Ordinal) == true)
            .SelectMany(e =>
                (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [HttpMethods.Get]).Select(method =>
                    (method, Fill(e.RoutePattern))
                )
            )
            .ToList();

    private static string Fill(RoutePattern pattern) =>
        string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(SampleOf))));

    private static string SampleOf(RoutePatternPart part) =>
        part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => SampleOf(parameter),
            _ => throw new InvalidOperationException(part.GetType().Name),
        };

    private static string SampleOf(RoutePatternParameterPart parameter)
    {
        var policies = string.Join(' ', parameter.ParameterPolicies.Select(p => p.Content));
        if (HexLength().Match(policies) is { Success: true } hex)
            return new string('0', int.Parse(hex.Groups[1].Value));
        if (policies.Contains("int", StringComparison.Ordinal) || policies.Contains("long", StringComparison.Ordinal))
            return "1";
        if (parameter.IsCatchAll)
            return SamplePath;
        return parameter.Name == "kind" ? "dropbox" : "x";
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex HexLength();

    private sealed class SettableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
