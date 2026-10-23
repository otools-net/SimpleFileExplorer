using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Setup, sign-in, rights, guests and the external players' links, through HTTP.</summary>
[TestClass]
public sealed class AccessTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            disposable.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task WithoutAUsableSetupCodeOnlyTheInstructionsWork()
    {
        // No code configured, and none can be made (its folder is a file): the page can only say how to set one.
        var blocker = Path.Combine(_library.BasePath, "not-a-folder");
        File.WriteAllText(blocker, "");
        var factory = Factory(settings =>
        {
            settings["Sfe:Setup:Code"] = "";
            settings["Sfe:Setup:CodeFile"] = Path.Combine(blocker, "setup-code.txt");
        });
        var client = Client(factory);

        var setup = await GetAsync<SetupInfo>(client, "/api/setup");
        using var config = await client.GetAsync("/api/config", TestContext.CancellationToken);
        using var attempt = await client.PostAsJsonAsync("/api/setup", new SetupInput("", "admin", "admin-password"), TestContext.CancellationToken);

        Assert.IsTrue(setup.Required);
        Assert.IsFalse(setup.CodeConfigured);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, config.StatusCode);
        StringAssert.Contains(await config.Content.ReadAsStringAsync(TestContext.CancellationToken), RequestGuard.SetupRequiredType);
        Assert.AreEqual(HttpStatusCode.Conflict, attempt.StatusCode);
    }

    [TestMethod]
    public async Task ShortSetupCodesAreNotAccepted()
    {
        var client = Client(Factory(settings => settings["Sfe:Setup:Code"] = "short"));

        var setup = await GetAsync<SetupInfo>(client, "/api/setup");
        using var attempt = await client.PostAsJsonAsync("/api/setup", new SetupInput("short", "admin", "admin-password"), TestContext.CancellationToken);

        Assert.IsFalse(setup.CodeConfigured);
        Assert.IsTrue(setup.CodeTooShort);
        Assert.AreEqual(HttpStatusCode.Conflict, attempt.StatusCode);
    }

    [TestMethod]
    public async Task TheSetupNeedsTheCodeAndHappensOnce()
    {
        var factory = Factory();
        var client = Client(factory);

        using var wrong = await client.PostAsJsonAsync("/api/setup", new SetupInput("not-the-code-at-all", "admin", "admin-password"), TestContext.CancellationToken);
        using var weak = await client.PostAsJsonAsync("/api/setup", new SetupInput(TestLibrary.SetupCode, "admin", "short"), TestContext.CancellationToken);
        using var done = await client.PostAsJsonAsync("/api/setup",
            new SetupInput(TestLibrary.SetupCode, "admin", "admin-password", "The Admin", "Family media"), TestContext.CancellationToken);
        var session = (await done.Content.ReadFromJsonAsync<SessionInfo>(Json, TestContext.CancellationToken))!;
        using var again = await Client(factory).PostAsJsonAsync("/api/setup", new SetupInput(TestLibrary.SetupCode, "other", "other-password"), TestContext.CancellationToken);
        var config = await GetAsync<ClientConfig>(client, "/api/config");
        var accounts = await GetAsync<List<UserInfo>>(client, "/api/access/users?all=true");

        Assert.AreEqual(HttpStatusCode.BadRequest, wrong.StatusCode);
        StringAssert.Contains(await wrong.Content.ReadAsStringAsync(TestContext.CancellationToken), "code");
        Assert.AreEqual(HttpStatusCode.BadRequest, weak.StatusCode);
        StringAssert.Contains(await weak.Content.ReadAsStringAsync(TestContext.CancellationToken), "password");
        Assert.AreEqual(HttpStatusCode.OK, done.StatusCode);
        Assert.AreEqual("admin", session.User?.UserName);
        Assert.IsTrue(session.User!.IsServerAdmin);
        Assert.AreEqual("Family media", session.Tenant?.Name);
        Assert.IsFalse(session.SetupRequired);
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);
        Assert.AreEqual("Family media", config.Tenant?.Name);
        CollectionAssert.IsSubsetOf(RightCatalog.All.Select(r => r.Id).ToList(), config.Rights.ToList());
        Assert.IsNotNull(accounts.Single().LastSignIn, "The setup signs the administrator in");
    }

    [TestMethod]
    public async Task RequestsChangingSomethingNeedTheRequestHeader()
    {
        var factory = Factory();
        var client = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        client.DefaultRequestHeaders.Remove(RequestGuard.Header);

        using var refused = await client.PostAsJsonAsync("/api/tags", new TagInputLike("Holiday"), TestContext.CancellationToken);
        using var read = await client.GetAsync("/api/config", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        StringAssert.Contains(await refused.Content.ReadAsStringAsync(TestContext.CancellationToken), RequestGuard.Header);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
    }

    [TestMethod]
    public async Task EveryRouteNamesARight()
    {
        var factory = Factory();
        Client(factory);

        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/') is { } path && (path.StartsWith("api/", StringComparison.Ordinal) || path.StartsWith("openapi", StringComparison.Ordinal)))
            .ToList();
        var unnamed = routes
            .Where(e => e.Metadata.GetMetadata<RightMetadata>() is null && e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        var unknown = routes
            .SelectMany(e => e.Metadata.GetOrderedMetadata<RightMetadata>().SelectMany(m => m.AnyOf))
            .Where(right => RightCatalog.Find(right) is null)
            .Distinct()
            .ToList();

        Assert.IsGreaterThan(50, routes.Count);
        Assert.IsEmpty(unnamed, "Routes without a right: " + string.Join(", ", unnamed));
        Assert.IsEmpty(unknown, "Rights not in the catalog: " + string.Join(", ", unknown));
    }

    /// <summary>
    /// A route taking a path (a <c>path</c> or <c>paths</c> parameter, or a body with a <c>Path</c>, <c>Paths</c> or
    /// <c>Keep</c>) says how it applies the folder rules, or that its paths are the server's own folders.
    /// </summary>
    [TestMethod]
    public async Task EveryRouteTakingAPathSaysHowFolderRulesApply()
    {
        var factory = Factory();
        Client(factory);
        string[] pathNames = ["path", "paths", "keep"];

        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("api/", StringComparison.Ordinal) == true)
            .Where(e => e.Metadata.GetMetadata<System.Reflection.MethodInfo>() is { } handler
                && handler.GetParameters().Any(p => (pathNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && typeof(IEnumerable<char>).IsAssignableFrom(p.ParameterType))
                    // Request bodies are the app's records (HttpRequest and some services have a Path of their own).
                    || (p.ParameterType.Namespace?.StartsWith("Sfe.", StringComparison.Ordinal) == true && p.ParameterType.GetMethod("<Clone>$") is not null
                        && p.ParameterType.GetProperties().Any(property => pathNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase)))))
            .ToList();
        var silent = routes
            .Where(e => e.Metadata.GetMetadata<FolderMetadata>() is null)
            .Select(e => $"{string.Join('/', e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .ToList();

        Assert.IsGreaterThan(25, routes.Count);
        Assert.IsEmpty(silent, "Routes taking a path without FolderScoped or NotLibraryPaths: " + string.Join(", ", silent));
    }

    [TestMethod]
    [DataRow("/api/thumbnails/{**path}", RequestLimits.Media)]
    [DataRow("/api/browse/covers", RequestLimits.Media)]
    [DataRow("/api/streaming/probe", RequestLimits.Media)]
    [DataRow("/api/streaming/play", RequestLimits.Media)]
    [DataRow("/api/trickplay/states", RequestLimits.Media)]
    [DataRow("/api/trickplay/files/{**path}", RequestLimits.Media)]
    [DataRow("/api/search", RequestLimits.Search)]
    [DataRow("/api/editor/images", RequestLimits.Upload)]
    public void ExpensiveRoutesAreLimitedPerCaller(string route, string policy)
    {
        var factory = Factory();
        Client(factory);

        // Every method of the route (the editor saves a copy with POST, over the picture with PUT).
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText == route).ToList();

        Assert.IsNotEmpty(endpoints, route);
        foreach (var endpoint in endpoints)
            Assert.AreEqual(policy, endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName, route);
    }

    [TestMethod]
    public async Task OneCallersFloodWaitsForItsOwnTurn()
    {
        var factory = Factory();
        var flooder = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        _disposables.Add(flooder);
        await CreateUserAsync(flooder, new UserInput("bob", Password: "bob-password", GroupIds: [(await GroupAsync(flooder, BuiltInGroup.Users)).Id]));
        // Another account, from the same address: limits follow the account.
        var other = await ApiClients.SignInAsync(factory, "bob", "bob-password", TestContext.CancellationToken);
        _disposables.Add(other);

        // As many searches as the limit allows a minute, then one more: the flood is refused, others are not.
        await Task.WhenAll(Enumerable.Range(0, RequestLimits.SearchesPerMinute).Select(async _ =>
            (await flooder.GetAsync("/api/search?q=x", TestContext.CancellationToken)).Dispose()));
        using var refused = await flooder.GetAsync("/api/search?q=x", TestContext.CancellationToken);
        using var served = await other.GetAsync("/api/search?q=x", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.TooManyRequests, served.StatusCode);
    }

    [TestMethod]
    public async Task SignInChecksThePasswordAndLocksAfterRepeatedFailures()
    {
        var factory = Factory(settings => settings["Sfe:Security:MaxFailedSignIns"] = "3");
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var client = Client(factory);

        using var unknown = await SignInAsync(client, "nobody", "whatever-password");
        var failures = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var wrong = await SignInAsync(client, ApiClients.AdminName, "wrong-password");
            failures.Add(wrong.StatusCode);
        }

        using var locked = await SignInAsync(client, ApiClients.AdminName, ApiClients.AdminPassword);

        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.IsTrue(failures.All(s => s == HttpStatusCode.BadRequest));
        Assert.AreEqual(HttpStatusCode.BadRequest, locked.StatusCode);
        StringAssert.Contains(await locked.Content.ReadAsStringAsync(TestContext.CancellationToken), "Too many failed attempts");
        Assert.IsNull((await GetAsync<SessionInfo>(client, "/api/session")).User);
    }

    [TestMethod]
    public async Task AStrangersFailuresDontLockTheOwnerOut()
    {
        var factory = Factory(settings => settings["Sfe:Security:MaxFailedSignIns"] = "3");
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var attacker = ApiClients.CreateFrom(factory, "203.0.113.7");
        var owner = ApiClients.CreateFrom(factory, "192.168.1.20");
        _disposables.Add(attacker);
        _disposables.Add(owner);

        for (var i = 0; i < 3; i++)
            (await SignInAsync(attacker, ApiClients.AdminName, "wrong-password")).Dispose();
        using var attackerLocked = await SignInAsync(attacker, ApiClients.AdminName, ApiClients.AdminPassword);
        using var ownerSignsIn = await SignInAsync(owner, ApiClients.AdminName, ApiClients.AdminPassword);

        Assert.AreEqual(HttpStatusCode.BadRequest, attackerLocked.StatusCode);
        StringAssert.Contains(await attackerLocked.Content.ReadAsStringAsync(TestContext.CancellationToken), "Too many failed attempts");
        Assert.AreEqual(HttpStatusCode.OK, ownerSignsIn.StatusCode);
    }

    [TestMethod]
    public async Task UnknownNamesAreThrottledAsKnownOnes()
    {
        var factory = Factory(settings => settings["Sfe:Security:MaxFailedSignIns"] = "2");
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var client = ApiClients.CreateFrom(factory, "203.0.113.8");
        _disposables.Add(client);

        async Task<string> ThirdAnswerAsync(string name)
        {
            for (var i = 0; i < 2; i++)
                (await SignInAsync(client, name, "wrong-password")).Dispose();
            using var third = await SignInAsync(client, name, "wrong-password");
            return await third.Content.ReadAsStringAsync(TestContext.CancellationToken);
        }

        StringAssert.Contains(await ThirdAnswerAsync(ApiClients.AdminName), "Too many failed attempts");
        StringAssert.Contains(await ThirdAnswerAsync("nobody"), "Too many failed attempts", "An unknown name answers as a known one");
    }

    [TestMethod]
    public async Task GuessesFromManyAddressesLockTheAccount()
    {
        var factory = Factory(settings => settings["Sfe:Security:AccountLockoutFailures"] = "4");
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        for (var i = 0; i < 4; i++)
        {
            using var client = ApiClients.CreateFrom(factory, $"203.0.113.{10 + i}");
            (await SignInAsync(client, ApiClients.AdminName, "wrong-password")).Dispose();
        }

        using var owner = ApiClients.CreateFrom(factory, "192.168.1.20");
        using var locked = await SignInAsync(owner, ApiClients.AdminName, ApiClients.AdminPassword);

        StringAssert.Contains(await locked.Content.ReadAsStringAsync(TestContext.CancellationToken), "Too many failed attempts");
    }

    private string RecoveryFile => Path.Combine(_library.BasePath, "recovery-code.txt");

    /// <summary>The code the server wrote for <paramref name="userName"/>, as its administrator reads it from the file.</summary>
    private string RecoveryCodeOf(string userName) =>
        File.ReadAllLines(RecoveryFile).Single(l => l.StartsWith(userName + ": ", StringComparison.Ordinal)).Split(' ')[1];

    [TestMethod]
    public async Task ALockedOutAdministratorRecoversWithACodeFromTheConsole()
    {
        var factory = Factory(settings => settings["Sfe:Security:AccountLockoutFailures"] = "3");
        var oldSession = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        _disposables.Add(oldSession);
        // Guesses from many addresses lock the account itself.
        for (var i = 0; i < 3; i++)
        {
            using var attacker = ApiClients.CreateFrom(factory, $"203.0.113.{40 + i}");
            (await SignInAsync(attacker, ApiClients.AdminName, "wrong-password")).Dispose();
        }

        var owner = ApiClients.CreateFrom(factory, "192.168.1.20");
        _disposables.Add(owner);
        using (var locked = await SignInAsync(owner, ApiClients.AdminName, ApiClients.AdminPassword))
            StringAssert.Contains(await locked.Content.ReadAsStringAsync(TestContext.CancellationToken), "Too many failed attempts");

        using var asked = await owner.PostAsJsonAsync("/api/session/recovery", new RecoveryRequest(ApiClients.AdminName), TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, asked.StatusCode, await asked.Content.ReadAsStringAsync(TestContext.CancellationToken));
        var code = RecoveryCodeOf(ApiClients.AdminName);
        using var askedAgain = await owner.PostAsJsonAsync("/api/session/recovery", new RecoveryRequest(ApiClients.AdminName), TestContext.CancellationToken);
        var codeAgain = RecoveryCodeOf(ApiClients.AdminName);
        using var wrongCode = await owner.PostAsJsonAsync("/api/session/recovery/complete",
            new RecoveryCompletion(ApiClients.AdminName, "AAAA-BBBB-CCCC-DDDD", "new-admin-password"), TestContext.CancellationToken);
        using var shortPassword = await owner.PostAsJsonAsync("/api/session/recovery/complete",
            new RecoveryCompletion(ApiClients.AdminName, code, "short"), TestContext.CancellationToken);
        // Retyped from the console: lower case, without the dashes.
        using var recovered = await owner.PostAsJsonAsync("/api/session/recovery/complete",
            new RecoveryCompletion(ApiClients.AdminName, code.Replace("-", "").ToLowerInvariant(), "new-admin-password"), TestContext.CancellationToken);
        var session = (await recovered.Content.ReadFromJsonAsync<SessionInfo>(Json, TestContext.CancellationToken))!;
        using var reused = await owner.PostAsJsonAsync("/api/session/recovery/complete",
            new RecoveryCompletion(ApiClients.AdminName, code, "another-password"), TestContext.CancellationToken);
        using var oldSessionAfter = await oldSession.GetAsync("/api/config", TestContext.CancellationToken);
        using var newPassword = await SignInAsync(Client(factory), ApiClients.AdminName, "new-admin-password");

        Assert.AreEqual(HttpStatusCode.OK, asked.StatusCode);
        Assert.AreEqual(code, codeAgain, "Asking again shows the same code: nobody can replace it");
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongCode.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        StringAssert.Contains(await shortPassword.Content.ReadAsStringAsync(TestContext.CancellationToken), "password");
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, "A wrong attempt doesn't use the code up");
        Assert.AreEqual(ApiClients.AdminName, session.User?.UserName);
        Assert.AreEqual(HttpStatusCode.BadRequest, reused.StatusCode, "A code works once");
        Assert.IsFalse(File.Exists(RecoveryFile), "No code left");
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldSessionAfter.StatusCode, "The other sessions end");
        Assert.AreEqual(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [TestMethod]
    public async Task RecoveryAnswersTheSameForEveryNameAndOnlyServesAdministrators()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        _disposables.Add(admin);
        await CreateUserAsync(admin, new UserInput("alice", Password: "alice-password", GroupIds: [(await GroupAsync(admin, BuiltInGroup.Administrators)).Id]));
        var visitor = Client(factory);

        using var unknown = await visitor.PostAsJsonAsync("/api/session/recovery", new RecoveryRequest("nobody"), TestContext.CancellationToken);
        // A space's administrator, not the server's: another administrator unlocks it.
        using var notServerAdmin = await visitor.PostAsJsonAsync("/api/session/recovery", new RecoveryRequest("alice"), TestContext.CancellationToken);
        using var completeUnknown = await visitor.PostAsJsonAsync("/api/session/recovery/complete",
            new RecoveryCompletion("alice", "AAAA-BBBB-CCCC-DDDD", "new-alice-password"), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, unknown.StatusCode);
        Assert.AreEqual(await unknown.Content.ReadAsStringAsync(TestContext.CancellationToken),
            await notServerAdmin.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(RecoveryFile));
        Assert.AreEqual(HttpStatusCode.BadRequest, completeUnknown.StatusCode);
    }

    [TestMethod]
    public async Task OnlyTrustedProxiesTellTheVisitorsAddressAndScheme()
    {
        var factory = Factory(settings => settings["Sfe:Security:SignInsPerMinute"] = "1");
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        // Behind a proxy on this computer: each visitor has their own limit, and https at the proxy secures the cookie.
        HttpClient ThroughProxy(string proxy, string visitor)
        {
            var client = ApiClients.CreateFrom(factory, proxy);
            client.DefaultRequestHeaders.Add("X-Forwarded-For", visitor);
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            _disposables.Add(client);
            return client;
        }

        using var first = await SignInAsync(ThroughProxy("127.0.0.1", "198.51.100.1"), ApiClients.AdminName, ApiClients.AdminPassword);
        using var second = await SignInAsync(ThroughProxy("127.0.0.1", "198.51.100.2"), ApiClients.AdminName, ApiClients.AdminPassword);
        // Not a proxy: what it says about the visitor is ignored.
        var stranger = ThroughProxy("203.0.113.9", "198.51.100.3");
        using var spoofed = await SignInAsync(stranger, ApiClients.AdminName, ApiClients.AdminPassword);
        using var spoofedAgain = await SignInAsync(ThroughProxy("203.0.113.9", "198.51.100.4"), ApiClients.AdminName, ApiClients.AdminPassword);

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode, "Another visitor of the same proxy");
        StringAssert.Contains(string.Join(";", first.Headers.GetValues("Set-Cookie")), "secure", StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(HttpStatusCode.OK, spoofed.StatusCode);
        Assert.DoesNotContain("secure", string.Join(";", spoofed.Headers.GetValues("Set-Cookie")).ToLowerInvariant());
        Assert.AreEqual(HttpStatusCode.TooManyRequests, spoofedAgain.StatusCode, "Its own address counts, whatever it forwards");
    }

    [TestMethod]
    public async Task RequireHttpsSecuresTheCookie()
    {
        var factory = Factory(settings => settings["Sfe:Security:RequireHttps"] = "true");
        (await ApiClients.SetUpAsync(factory, TestContext.CancellationToken)).Dispose();
        var client = Client(factory);

        using var signIn = await SignInAsync(client, ApiClients.AdminName, ApiClients.AdminPassword);

        StringAssert.Contains(string.Join(";", signIn.Headers.GetValues("Set-Cookie")), "secure", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task SignInAndOutChangeTheSession()
    {
        var factory = Factory();
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var client = Client(factory);

        using var anonymous = await client.GetAsync("/api/config", TestContext.CancellationToken);
        using var signIn = await SignInAsync(client, "ADMIN", ApiClients.AdminPassword);
        var session = (await signIn.Content.ReadFromJsonAsync<SessionInfo>(Json, TestContext.CancellationToken))!;
        using var signedIn = await client.GetAsync("/api/config", TestContext.CancellationToken);
        using var signOut = await client.PostAsync("/api/session/logout", null, TestContext.CancellationToken);
        using var after = await client.GetAsync("/api/config", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, signIn.StatusCode, "Names are matched whatever the case");
        Assert.AreEqual(ApiClients.AdminName, session.User?.UserName);
        Assert.IsTrue(session.CanBrowse);
        Assert.AreEqual(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, signOut.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [TestMethod]
    public async Task TheSessionSurvivesARestart()
    {
        string cookie;
        using (var first = Factory())
        {
            var client = first.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            client.DefaultRequestHeaders.Add(RequestGuard.Header, "1");
            using var setup = await client.PostAsJsonAsync("/api/setup", new SetupInput(TestLibrary.SetupCode, "admin", "admin-password"), TestContext.CancellationToken);
            cookie = setup.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(SfeSession.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
        }

        var second = Factory();
        var restarted = second.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        restarted.DefaultRequestHeaders.Add("Cookie", cookie);

        var session = await GetAsync<SessionInfo>(restarted, "/api/session");

        Assert.AreEqual("admin", session.User?.UserName, "The keys are kept on the disk (Sfe:Security:KeysDirectory)");
    }

    [TestMethod]
    public async Task EachPortHasASessionCookieOfItsOwn()
    {
        // Browsers keep cookies per host, not per port: two instances on one host must not share the session's.
        var factory = Factory();
        var client = PortClient(factory, 5000);
        using var setup = await client.PostAsJsonAsync("/api/setup", new SetupInput(TestLibrary.SetupCode, "admin", "admin-password"), TestContext.CancellationToken);
        var cookie = SetCookie(setup, "sfe.session-5000");
        Assert.IsNotNull(cookie, string.Join(" | ", setup.Headers.GetValues("Set-Cookie")));
        Assert.IsNull(SetCookie(setup, SfeSession.CookieName), "Not under the shared name");

        var same = PortClient(factory, 5000);
        same.DefaultRequestHeaders.Add("Cookie", cookie);
        var other = PortClient(factory, 5001);
        other.DefaultRequestHeaders.Add("Cookie", cookie);

        Assert.AreEqual("admin", (await GetAsync<SessionInfo>(same, "/api/session")).User?.UserName);
        Assert.IsNull((await GetAsync<SessionInfo>(other, "/api/session")).User, "Another port's cookie isn't this one's");
    }

    [TestMethod]
    public async Task ASessionUnderTheSharedNameIsKeptAndRenamed()
    {
        // A session signed in before the names had ports: read once, then written under this port's name.
        var factory = Factory();
        var client = PortClient(factory, 5000);
        using var setup = await client.PostAsJsonAsync("/api/setup", new SetupInput(TestLibrary.SetupCode, "admin", "admin-password"), TestContext.CancellationToken);
        var value = SetCookie(setup, "sfe.session-5000")!.Split('=', 2)[1];

        var before = PortClient(factory, 5000);
        before.DefaultRequestHeaders.Add("Cookie", $"{SfeSession.CookieName}={value}");
        using var response = await before.GetAsync("/api/session", TestContext.CancellationToken);
        var session = (await response.Content.ReadFromJsonAsync<SessionInfo>(Json, TestContext.CancellationToken))!;

        Assert.AreEqual("admin", session.User?.UserName);
        Assert.IsNotNull(SetCookie(response, "sfe.session-5000"), "Written under the new name");
        var removed = response.Headers.GetValues("Set-Cookie").SingleOrDefault(c => c.StartsWith(SfeSession.CookieName + "=;", StringComparison.Ordinal));
        Assert.IsNotNull(removed, "The shared one is removed");
        StringAssert.Contains(removed, "expires=Thu, 01 Jan 1970");

        // One this instance can't read (another instance's) is left alone.
        var foreign = PortClient(factory, 5000);
        foreign.DefaultRequestHeaders.Add("Cookie", $"{SfeSession.CookieName}=another-instances-session");
        using var left = await foreign.GetAsync("/api/session", TestContext.CancellationToken);
        Assert.IsNull((await left.Content.ReadFromJsonAsync<SessionInfo>(Json, TestContext.CancellationToken))!.User);
        Assert.IsFalse(left.Headers.TryGetValues("Set-Cookie", out var set) && set.Any(c => c.StartsWith(SfeSession.CookieName + "=", StringComparison.Ordinal)));
    }

    private static HttpClient PortClient(WebApplicationFactory<Program> factory, int port)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://localhost:{port}"), HandleCookies = false });
        client.DefaultRequestHeaders.Add(RequestGuard.Header, "1");
        return client;
    }

    /// <summary>The <c>name=value</c> of the cookie <paramref name="name"/> a response sets, if it sets it.</summary>
    private static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal) && !c.StartsWith(name + "=;", StringComparison.Ordinal))?.Split(';')[0]
            : null;

    [TestMethod]
    public async Task UsersOnlyGetTheRightsOfTheirGroups()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var users = await GroupAsync(admin, BuiltInGroup.Users);
        await CreateUserAsync(admin, new UserInput("alice", Password: "alice-password", GroupIds: [users.Id]));
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", TestContext.CancellationToken);

        var config = await GetAsync<ClientConfig>(alice, "/api/config");
        using var browse = await alice.GetAsync("/api/browse", TestContext.CancellationToken);
        using var scan = await alice.PostAsJsonAsync("/api/scan", new ScanRequest(""), TestContext.CancellationToken);
        using var settings = await alice.GetAsync("/api/settings", TestContext.CancellationToken);
        using var accounts = await alice.GetAsync("/api/access/users", TestContext.CancellationToken);
        using var tag = await alice.PostAsJsonAsync("/api/tags", new TagInputLike("Holiday"), TestContext.CancellationToken);

        CollectionAssert.AreEquivalent(
            RightCatalog.All.Where(r => r.DefaultGroups.Contains(BuiltInGroup.Users)).Select(r => r.Id).ToList(),
            config.Rights.ToList());
        Assert.IsNull(config.RootPath);
        Assert.AreEqual(HttpStatusCode.OK, browse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, scan.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, settings.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, accounts.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, tag.StatusCode);
    }

    [TestMethod]
    public async Task GuestsNeverGetRightsThatChangeSomething()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var guests = await GroupAsync(admin, BuiltInGroup.Guests);
        var administrators = await GroupAsync(admin, BuiltInGroup.Administrators);

        using var refused = await admin.PutAsJsonAsync($"/api/access/groups/{guests.Id}",
            new GroupInput(guests.Name, Rights: [.. guests.Rights, Rights.ManageTags]), Json, TestContext.CancellationToken);
        // A guest account in the Administrators group still only gets what guests may have.
        await CreateUserAsync(admin, new UserInput("kids", Password: "kids-password", Kind: UserKind.Guest, GroupIds: [administrators.Id]));
        var kids = await ApiClients.SignInAsync(factory, "kids", "kids-password", TestContext.CancellationToken);
        var config = await GetAsync<ClientConfig>(kids, "/api/config");
        using var scan = await kids.PostAsJsonAsync("/api/scan", new ScanRequest(""), TestContext.CancellationToken);
        using var guestAdmin = await admin.PostAsJsonAsync("/api/access/users",
            new UserInput("bad", Password: "bad-password", Kind: UserKind.Guest, IsServerAdmin: true), Json, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        CollectionAssert.AreEquivalent(RightCatalog.All.Where(r => r.GuestAllowed).Select(r => r.Id).ToList(), config.Rights.ToList());
        Assert.AreEqual(HttpStatusCode.Forbidden, scan.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, guestAdmin.StatusCode);
    }

    [TestMethod]
    public async Task VisitorsBrowseOnlyLibrariesOpenToGuests()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var visitor = Client(factory);

        using var closed = await visitor.GetAsync("/api/config", TestContext.CancellationToken);
        var before = await GetAsync<SessionInfo>(visitor, "/api/session");
        using (var open = await admin.PutAsJsonAsync("/api/access/tenant/guest-access", new GuestAccessChange(true), TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, open.StatusCode);
        var after = await GetAsync<SessionInfo>(visitor, "/api/session");
        var config = await GetAsync<ClientConfig>(visitor, "/api/config");
        using var file = await visitor.GetAsync("/api/files/raw/Media/photo.jpg", TestContext.CancellationToken);
        using var download = await visitor.GetAsync("/api/files/raw/Media/photo.jpg?download=true", TestContext.CancellationToken);
        using var scan = await visitor.PostAsJsonAsync("/api/scan", new ScanRequest(""), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, closed.StatusCode);
        Assert.IsEmpty(before.GuestTenants);
        Assert.AreEqual("library", after.GuestTenants.Single().Slug);
        Assert.IsTrue(after.CanBrowse);
        Assert.IsNull(config.User);
        CollectionAssert.AreEquivalent(
            RightCatalog.All.Where(r => r.DefaultGroups.Contains(BuiltInGroup.Guests)).Select(r => r.Id).ToList(), config.Rights.ToList());
        Assert.AreEqual(HttpStatusCode.OK, file.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.OK, download.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, scan.StatusCode);
    }

    [TestMethod]
    public async Task TheServerCanForbidVisitorsWithoutAnAccount()
    {
        var factory = Factory(settings => settings["Sfe:Security:AnonymousGuests"] = "false");
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        using (await admin.PutAsJsonAsync("/api/access/tenant/guest-access", new GuestAccessChange(true), TestContext.CancellationToken)) { }

        using var config = await Client(factory).GetAsync("/api/config", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, config.StatusCode);
    }

    [TestMethod]
    public async Task RightsDisabledByTheServerAreRefusedEvenToAdministrators()
    {
        var factory = Factory(settings => settings["Sfe:Rights:Disabled:0"] = "tracking.*");
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        using var duplicates = await admin.GetAsync("/api/duplicates", TestContext.CancellationToken);
        var config = await GetAsync<ClientConfig>(admin, "/api/config");
        var catalog = await GetAsync<RightsCatalogInfo>(admin, "/api/access/rights");

        Assert.AreEqual(HttpStatusCode.Forbidden, duplicates.StatusCode);
        Assert.IsFalse(config.Rights.Any(r => r.StartsWith("tracking.", StringComparison.Ordinal)));
        CollectionAssert.Contains(catalog.DisabledByServer.ToList(), Rights.RemoveDuplicates);
    }

    [TestMethod]
    public async Task TheServerCanSwitchOffEveryRightThatChangesFiles()
    {
        var factory = Factory(settings => settings["Sfe:Rights:Disabled:0"] = RightCatalog.FileWriteSelector);
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        var config = await GetAsync<ClientConfig>(admin, "/api/config");
        var catalog = await GetAsync<RightsCatalogInfo>(admin, "/api/access/rights");

        CollectionAssert.AreEquivalent(new[] { Rights.EditImages, Rights.RemoveDuplicates, Rights.CreateFolders, Rights.RenameFiles, Rights.DeleteFiles, Rights.DeletePermanently, Rights.MoveFiles, Rights.CopyFiles, Rights.UploadFiles, Rights.CreateFiles }, catalog.DisabledByServer.ToList());
        CollectionAssert.DoesNotContain(config.Rights.ToList(), Rights.EditImages);
        CollectionAssert.Contains(config.Rights.ToList(), Rights.AssignTags);
        Assert.AreEqual(FolderLevel.Write, catalog.Rights.Single(r => r.Id == Rights.EditImages).AtFolder);
        Assert.AreEqual(RightKind.Write, catalog.Rights.Single(r => r.Id == Rights.AssignTags).Kind);
    }

    [TestMethod]
    public async Task TheServerCanBeMadeReadOnly()
    {
        var factory = Factory(settings => settings["Sfe:Rights:Disabled:0"] = RightCatalog.WriteSelector);
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        var config = await GetAsync<ClientConfig>(admin, "/api/config");
        using var tag = await admin.PostAsJsonAsync("/api/file-tags", new { paths = Array.Empty<string>(), add = Array.Empty<int>() }, TestContext.CancellationToken);

        Assert.IsTrue(config.Rights.All(r => RightCatalog.Find(r)!.Kind == RightKind.Read), string.Join(", ", config.Rights));
        CollectionAssert.Contains(config.Rights.ToList(), Rights.Browse);
        Assert.AreEqual(HttpStatusCode.Forbidden, tag.StatusCode);
    }

    [TestMethod]
    public async Task ChangingAnAccountEndsItsSessions()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var users = await GroupAsync(admin, BuiltInGroup.Users);
        var alice = await CreateUserAsync(admin, new UserInput("alice", Password: "alice-password", GroupIds: [users.Id]));
        var aliceClient = await ApiClients.SignInAsync(factory, "alice", "alice-password", TestContext.CancellationToken);

        using var before = await aliceClient.GetAsync("/api/config", TestContext.CancellationToken);
        using (var disabled = await admin.PutAsJsonAsync($"/api/access/users/{alice.Id}",
                   new UserInput("alice", Disabled: true, GroupIds: [users.Id]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, disabled.StatusCode);
        using var after = await aliceClient.GetAsync("/api/config", TestContext.CancellationToken);
        using var signIn = await SignInAsync(Client(factory), "alice", "alice-password");

        Assert.AreEqual(HttpStatusCode.OK, before.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, after.StatusCode);
        StringAssert.Contains(await signIn.Content.ReadAsStringAsync(TestContext.CancellationToken), "disabled");
    }

    [TestMethod]
    public async Task OwnPasswordChangesKeepTheSession()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        using var wrong = await admin.PutAsJsonAsync("/api/session/password", new PasswordChange("not-it", "new-password-1"), TestContext.CancellationToken);
        using var changed = await admin.PutAsJsonAsync("/api/session/password", new PasswordChange(ApiClients.AdminPassword, "new-password-1"), TestContext.CancellationToken);
        using var still = await admin.GetAsync("/api/config", TestContext.CancellationToken);
        using var old = await SignInAsync(Client(factory), ApiClients.AdminName, ApiClients.AdminPassword);
        using var fresh = await SignInAsync(Client(factory), ApiClients.AdminName, "new-password-1");

        Assert.AreEqual(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, still.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, old.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, fresh.StatusCode);
    }

    [TestMethod]
    public async Task TheApiDocumentationNeedsItsRight()
    {
        var factory = Factory();
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var visitor = Client(factory);

        using var page = await visitor.GetAsync("/api/docs/index.html", TestContext.CancellationToken);
        using var document = await visitor.GetAsync("/openapi/v1.json", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, page.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, document.StatusCode);
    }

    [TestMethod]
    public async Task ExternalPlayerLinksWorkWithoutSignInUntilRevoked()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        using var created = await admin.PostAsJsonAsync("/api/files/links", new FileLinkRequest("Media/Episode 2.mkv"), TestContext.CancellationToken);
        var link = (await created.Content.ReadFromJsonAsync<FileLink>(Json, TestContext.CancellationToken))!;
        using var missing = await admin.PostAsJsonAsync("/api/files/links", new FileLinkRequest("Media/nothing.mkv"), TestContext.CancellationToken);
        var player = Client(factory);
        using var whole = await player.GetAsync("/" + link.Url, TestContext.CancellationToken);
        using var range = new HttpRequestMessage(HttpMethod.Get, "/" + link.Url) { Headers = { Range = new RangeHeaderValue(0, 99) } };
        using var part = await player.SendAsync(range, TestContext.CancellationToken);
        var token = link.Url.Split('/')[3];
        using var forged = await player.GetAsync("/" + link.Url.Replace(token, token[..^2] + (token[^2] == 'A' ? "BB" : "AA")), TestContext.CancellationToken);
        using (await admin.PutAsJsonAsync("/api/session/password", new PasswordChange(ApiClients.AdminPassword, "new-password-1"), TestContext.CancellationToken)) { }
        using var revoked = await player.GetAsync("/" + link.Url, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
        StringAssert.EndsWith(link.Url, "/Episode%202.mkv");
        Assert.IsGreaterThan(DateTimeOffset.UtcNow.AddMinutes(300), link.Expires);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, whole.StatusCode);
        Assert.AreEqual(200 * 1024, (await whole.Content.ReadAsByteArrayAsync(TestContext.CancellationToken)).Length);
        Assert.AreEqual(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, forged.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [TestMethod]
    public async Task TheLastServerAdministratorStays()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var self = (await GetAsync<SessionInfo>(admin, "/api/session")).User!;

        using var demoted = await admin.PutAsJsonAsync($"/api/access/users/{self.Id}", new UserInput(self.UserName, IsServerAdmin: false), Json, TestContext.CancellationToken);
        using var deleted = await admin.DeleteAsync($"/api/access/users/{self.Id}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Conflict, demoted.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, deleted.StatusCode);
    }

    [TestMethod]
    public async Task RecoveryRestoresALocalAdministrator()
    {
        using (var first = Factory())
            await ApiClients.SetUpAsync(first, TestContext.CancellationToken);

        var recovering = Factory(settings => settings["Sfe:Setup:Recover"] = "true");
        var client = Client(recovering);
        var setup = await GetAsync<SetupInfo>(client, "/api/setup");
        using var browse = await Client(recovering).GetAsync("/api/session", TestContext.CancellationToken);
        using var restored = await client.PostAsJsonAsync("/api/setup", new SetupInput(TestLibrary.SetupCode, "admin", "recovered-password"), TestContext.CancellationToken);
        using var signIn = await SignInAsync(Client(recovering), "admin", "recovered-password");
        var users = await GetAsync<List<UserInfo>>(client, "/api/access/users?all=true");

        Assert.IsFalse(setup.Required, "The app keeps working during a recovery");
        Assert.IsTrue(setup.Available);
        Assert.IsTrue(setup.Recover);
        Assert.AreEqual(HttpStatusCode.OK, restored.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, signIn.StatusCode);
        Assert.AreEqual(1, users.Count, "The administrator was restored, not duplicated");
    }

    [TestMethod]
    public async Task LibraryAdministratorsOnlyManageTheirLibrarysAccounts()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var administrators = await GroupAsync(admin, BuiltInGroup.Administrators);
        var users = await GroupAsync(admin, BuiltInGroup.Users);
        await CreateUserAsync(admin, new UserInput("manager", Password: "manager-password", GroupIds: [administrators.Id]));
        var manager = await ApiClients.SignInAsync(factory, "manager", "manager-password", TestContext.CancellationToken);
        var self = (await GetAsync<SessionInfo>(admin, "/api/session")).User!;

        using var created = await manager.PostAsJsonAsync("/api/access/users",
            new UserInput("bob", Password: "bob-password", IsServerAdmin: true, GroupIds: [users.Id]), Json, TestContext.CancellationToken);
        var bob = (await created.Content.ReadFromJsonAsync<UserInfo>(Json, TestContext.CancellationToken))!;
        using var noGroup = await manager.PostAsJsonAsync("/api/access/users", new UserInput("carol", Password: "carol-password"), Json, TestContext.CancellationToken);
        using var serverSettings = await manager.GetAsync("/api/settings?level=server", TestContext.CancellationToken);
        using var libraryAdmin = await manager.PutAsJsonAsync($"/api/access/users/{self.Id}",
            new UserInput("renamed", Disabled: true, GroupIds: []), Json, TestContext.CancellationToken);
        var adminAfter = (await GetAsync<List<UserInfo>>(admin, "/api/access/users?all=true")).Single(u => u.Id == self.Id);

        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        Assert.IsFalse(bob.IsServerAdmin, "Only server administrators make server administrators");
        Assert.AreEqual(HttpStatusCode.BadRequest, noGroup.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, serverSettings.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, libraryAdmin.StatusCode);
        Assert.AreEqual(ApiClients.AdminName, adminAfter.UserName, "A server administrator's account is not the library's to change");
        Assert.IsFalse(adminAfter.Disabled);
    }

    [TestMethod]
    public async Task LibraryAdministratorsOnlyPutTheirLibrarysAccountsInGroups()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var administrators = await GroupAsync(admin, BuiltInGroup.Administrators);
        var users = await GroupAsync(admin, BuiltInGroup.Users);
        await CreateUserAsync(admin, new UserInput("manager", Password: "manager-password", GroupIds: [administrators.Id]));
        var member = await CreateUserAsync(admin, new UserInput("member", Password: "member-password", GroupIds: [users.Id]));
        var outsider = await CreateUserAsync(admin, new UserInput("outsider", Password: "outsider-password"));
        users = await GroupAsync(admin, BuiltInGroup.Users);
        var manager =await ApiClients.SignInAsync(factory, "manager", "manager-password", TestContext.CancellationToken);

        // Taken into a group, the account would belong to this library alone: its administrators could set its password.
        using var pulledIn = await manager.PutAsJsonAsync($"/api/access/groups/{users.Id}",
            new GroupInput(users.Name, MemberIds: [.. users.MemberIds, outsider.Id]), Json, TestContext.CancellationToken);
        using var newGroup = await manager.PostAsJsonAsync("/api/access/groups",
            new GroupInput("Friends", MemberIds: [outsider.Id]), Json, TestContext.CancellationToken);
        using var takenOver = await manager.PutAsJsonAsync($"/api/access/users/{outsider.Id}",
            new UserInput("outsider", Password: "taken-over", GroupIds: [users.Id]), Json, TestContext.CancellationToken);
        using var promoted = await manager.PutAsJsonAsync($"/api/access/groups/{administrators.Id}",
            new GroupInput(administrators.Name, MemberIds: [.. (await GroupAsync(admin, BuiltInGroup.Administrators)).MemberIds, member.Id]), Json, TestContext.CancellationToken);
        var outsiderAfter = (await GetAsync<List<UserInfo>>(admin, "/api/access/users?all=true")).Single(u => u.Id == outsider.Id);
        using var byServerAdmin = await admin.PutAsJsonAsync($"/api/access/groups/{users.Id}",
            new GroupInput(users.Name, MemberIds: [.. users.MemberIds, outsider.Id]), Json, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, pulledIn.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, newGroup.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, takenOver.StatusCode);
        Assert.IsEmpty(outsiderAfter.Groups, "The account stayed out of the library");
        Assert.AreEqual(HttpStatusCode.OK, promoted.StatusCode, "Accounts of the library still move between its groups");
        Assert.AreEqual(HttpStatusCode.OK, byServerAdmin.StatusCode, "Server administrators add any account");
    }

    [TestMethod]
    public void PasswordHashesVerifyAndTellOldOnes()
    {
        var hash = PasswordHashing.Hash("correct horse");
        var old = hash.Replace($"${PasswordHashing.Iterations}$", "$1000$");

        Assert.IsTrue(PasswordHashing.Verify("correct horse", hash, out var outdated));
        Assert.IsFalse(outdated);
        Assert.IsFalse(PasswordHashing.Verify("wrong horse", hash, out _));
        Assert.IsFalse(PasswordHashing.Verify("correct horse", "garbage", out _));
        Assert.IsFalse(PasswordHashing.Verify("correct horse", null, out _));
        Assert.IsFalse(PasswordHashing.Verify("correct horse", old, out _), "The iteration count is part of the hash");
        Assert.AreNotEqual(hash, PasswordHashing.Hash("correct horse"), "Salted");
    }

    [TestMethod]
    [DataRow("Family Photos (2020)", "family-photos-2020")]
    [DataRow("Películas", "peliculas")]
    [DataRow("Фильмы", "library")]
    [DataRow("--a--b--", "a-b")]
    public void LibrarySlugsAreMadeFromTheirNames(string name, string slug) => Assert.AreEqual(slug, AccessService.Slugify(name));

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        return factory;
    }

    private HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = ApiClients.Create(factory);
        _disposables.Add(client);
        return client;
    }

    private Task<HttpResponseMessage> SignInAsync(HttpClient client, string userName, string password) =>
        client.PostAsJsonAsync("/api/session/login", new SignInRequest(userName, password), TestContext.CancellationToken);

    private async Task<GroupInfo> GroupAsync(HttpClient admin, BuiltInGroup builtIn) =>
        (await GetAsync<List<GroupInfo>>(admin, "/api/access/groups")).Single(g => g.BuiltIn == builtIn);

    private async Task<UserInfo> CreateUserAsync(HttpClient admin, UserInput input)
    {
        using var response = await admin.PostAsJsonAsync("/api/access/users", input, Json, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<UserInfo>(Json, TestContext.CancellationToken))!;
    }

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {await response.Content.ReadAsStringAsync(TestContext.CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }

    /// <summary>The body of <c>POST /api/tags</c>.</summary>
    private sealed record TagInputLike(string Name);
}
