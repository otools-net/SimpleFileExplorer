using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api;
using Sfe.Api.Endpoints;
using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Settings;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// SFE used on one computer: the setup of a new install without the code from that computer
/// (<see cref="LocalVisitor"/>), the browser opened at start (<see cref="BrowserLauncher"/>) and its one-time sign-in
/// link (<see cref="FastSignIn"/>), through HTTP.
/// </summary>
[TestClass]
public sealed class LocalStartTests
{
    private const string ThisComputer = "127.0.0.1";
    private const string AnotherComputer = "203.0.113.7";

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
    public async Task ANewInstallIsSetUpFromThisComputerWithoutTheCode()
    {
        var factory = Factory();
        var local = ApiClients.CreateFrom(factory, ThisComputer);

        var info = await GetAsync<SetupInfo>(local, "/api/setup");
        using var check = await local.PostAsJsonAsync("/api/installation/check", new InstallationCheck(null), TestContext.CancellationToken);
        var grant = (await check.Content.ReadFromJsonAsync<InstallationGrant>(Json, TestContext.CancellationToken))!;
        using var installation = new HttpRequestMessage(HttpMethod.Get, "/api/installation");
        installation.Headers.Add(InstallationAccess.Header, grant.Token);
        using var settings = await local.SendAsync(installation, TestContext.CancellationToken);
        using var done = await local.PostAsJsonAsync("/api/setup", new SetupInput(null, "admin", "admin-password", FastSignIn: true), TestContext.CancellationToken);
        var session = (await done.Content.ReadFromJsonAsync<SessionInfo>(Json, TestContext.CancellationToken))!;

        Assert.IsFalse(info.CodeRequired);
        Assert.AreEqual(HttpStatusCode.OK, check.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, settings.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, done.StatusCode, await done.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(session.User!.IsServerAdmin);
        Assert.IsTrue(factory.Services.GetRequiredService<SettingLayers>().ServerLevel().FastSignIn, "The setup's choice is a server setting");
    }

    [TestMethod]
    public async Task OnceSetUpTheTokenWithoutCodeOpensNothing()
    {
        var factory = Factory();
        var local = ApiClients.CreateFrom(factory, ThisComputer);
        using var check = await local.PostAsJsonAsync("/api/installation/check", new InstallationCheck(null), TestContext.CancellationToken);
        var grant = (await check.Content.ReadFromJsonAsync<InstallationGrant>(Json, TestContext.CancellationToken))!;
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var admin = await SignInFromAsync(factory, ThisComputer);

        using var installation = new HttpRequestMessage(HttpMethod.Get, "/api/installation");
        installation.Headers.Add(InstallationAccess.Header, grant.Token);
        using var settings = await admin.SendAsync(installation, TestContext.CancellationToken);
        using var noCode = await admin.PostAsJsonAsync("/api/installation/check", new InstallationCheck(null), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, settings.StatusCode, "Even a server administrator needs the code once set up");
        Assert.AreEqual(HttpStatusCode.BadRequest, noCode.StatusCode);
    }

    [TestMethod]
    [DataRow(AnotherComputer, null, null, DisplayName = "another computer")]
    [DataRow(ThisComputer, "198.51.100.4", null, DisplayName = "through a proxy on this computer")]
    [DataRow(ThisComputer, null, "rebound.example", DisplayName = "a foreign host name (DNS rebinding)")]
    public async Task OtherVisitorsStillNeedTheCode(string address, string? forwardedFor, string? host)
    {
        var client = ApiClients.CreateFrom(Factory(), address);
        if (forwardedFor is not null)
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        if (host is not null)
            client.DefaultRequestHeaders.Host = host;

        var info = await GetAsync<SetupInfo>(client, "/api/setup");
        using var check = await client.PostAsJsonAsync("/api/installation/check", new InstallationCheck(null), TestContext.CancellationToken);
        using var setup = await client.PostAsJsonAsync("/api/setup", new SetupInput(null, "admin", "admin-password"), TestContext.CancellationToken);

        Assert.IsTrue(info.CodeRequired);
        Assert.AreEqual(HttpStatusCode.BadRequest, check.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, setup.StatusCode);
        StringAssert.Contains(await setup.Content.ReadAsStringAsync(TestContext.CancellationToken), "code");
    }

    [TestMethod]
    public async Task AnInstallWithAccountsOrTheSwitchOffNeedsTheCode()
    {
        // An account exists (the administrators were deleted or disabled, say): not a new install.
        var used = Factory();
        await used.Services.GetRequiredService<AccessService>().CreateUserAsync(new UserInput("someone", Password: "someone-password"), null, TestContext.CancellationToken);
        var switchedOff = Factory(settings => settings["Sfe:Setup:LocalWithoutCode"] = "false", library: OtherLibrary());

        var usedInfo = await GetAsync<SetupInfo>(ApiClients.CreateFrom(used, ThisComputer), "/api/setup");
        var offInfo = await GetAsync<SetupInfo>(ApiClients.CreateFrom(switchedOff, ThisComputer), "/api/setup");
        using var setup = await ApiClients.CreateFrom(used, ThisComputer)
            .PostAsJsonAsync("/api/setup", new SetupInput(null, "admin", "admin-password"), TestContext.CancellationToken);

        Assert.IsTrue(usedInfo.Required);
        Assert.IsTrue(usedInfo.CodeRequired);
        Assert.IsTrue(offInfo.CodeRequired);
        Assert.AreEqual(HttpStatusCode.BadRequest, setup.StatusCode);
    }

    [TestMethod]
    public async Task TheStartLinkSignsTheOnlyAdministratorInOnce()
    {
        var factory = Factory(settings => settings["Sfe:FastSignIn"] = "true");
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var token = await factory.Services.GetRequiredService<FastSignIn>().IssueAsync(TestContext.CancellationToken);
        var browser = Browser(factory, ThisComputer);

        using var start = await browser.GetAsync($"/{FastSignIn.Route}?token={token}", TestContext.CancellationToken);
        var session = await GetAsync<SessionInfo>(browser, "/api/session");
        var other = Browser(factory, ThisComputer);
        using var again = await other.GetAsync($"/{FastSignIn.Route}?token={token}", TestContext.CancellationToken);
        var otherSession = await GetAsync<SessionInfo>(other, "/api/session");

        Assert.IsNotNull(token);
        Assert.AreEqual(HttpStatusCode.Redirect, start.StatusCode);
        Assert.AreEqual("/", start.Headers.Location?.OriginalString);
        Assert.AreEqual(ApiClients.AdminName, session.User?.UserName);
        Assert.AreEqual(HttpStatusCode.Redirect, again.StatusCode);
        Assert.IsNull(otherSession.User, "The link works once");
    }

    [TestMethod]
    public async Task TheStartLinkOnlyWorksFromThisComputerAndInTime()
    {
        var time = new ShiftedTime();
        var factory = Factory(settings => settings["Sfe:FastSignIn"] = "true", time);
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var links = factory.Services.GetRequiredService<FastSignIn>();

        var remote = Browser(factory, AnotherComputer);
        using (await remote.GetAsync($"/{FastSignIn.Route}?token={await links.IssueAsync(TestContext.CancellationToken)}", TestContext.CancellationToken)) { }
        var remoteSession = await GetAsync<SessionInfo>(remote, "/api/session");

        var late = Browser(factory, ThisComputer);
        var token = await links.IssueAsync(TestContext.CancellationToken);
        time.Offset = FastSignIn.Lifetime + TimeSpan.FromSeconds(1);
        using (await late.GetAsync($"/{FastSignIn.Route}?token={token}", TestContext.CancellationToken)) { }
        var lateSession = await GetAsync<SessionInfo>(late, "/api/session");

        Assert.IsNull(remoteSession.User);
        Assert.IsNull(lateSession.User);
    }

    [TestMethod]
    public async Task NoStartLinkWhenOffOrForSeveralAdministrators()
    {
        var off = Factory(library: OtherLibrary());
        await ApiClients.SetUpAsync(off, TestContext.CancellationToken);
        var on = Factory(settings => settings["Sfe:FastSignIn"] = "true");
        var admin = await ApiClients.SetUpAsync(on, TestContext.CancellationToken);
        admin.DefaultRequestHeaders.Add(ApiClients.AddressHeader, ThisComputer);
        var links = on.Services.GetRequiredService<FastSignIn>();
        var pending = await links.IssueAsync(TestContext.CancellationToken);
        using (var created = await admin.PostAsJsonAsync("/api/access/users",
                   new UserInput("second", Password: "second-password", IsServerAdmin: true), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync(TestContext.CancellationToken));
        var browser = Browser(on, ThisComputer);
        using (await browser.GetAsync($"/{FastSignIn.Route}?token={pending}", TestContext.CancellationToken)) { }

        Assert.IsNull(await off.Services.GetRequiredService<FastSignIn>().IssueAsync(TestContext.CancellationToken));
        Assert.IsNull(await links.IssueAsync(TestContext.CancellationToken), "Two administrators: whose session would it be?");
        Assert.IsNull((await GetAsync<SessionInfo>(browser, "/api/session")).User, "A link given before the second administrator no longer works");
    }

    [TestMethod]
    public async Task AnExternalServerSetByItsConfigurationNeedsTheCodeAndHasNoStartLink()
    {
        var factory = Factory(settings => { settings[ServerModes.Key] = "External"; settings["Sfe:FastSignIn"] = "true"; });
        var local = ApiClients.CreateFrom(factory, ThisComputer);

        var info = await GetAsync<SetupInfo>(local, "/api/setup");
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var links = factory.Services.GetRequiredService<FastSignIn>();

        Assert.IsTrue(info.CodeRequired, "A server for others: whoever runs it gives the code");
        Assert.IsNull(await links.IssueAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ExternalChosenDuringASetupWithoutCodeKeepsItWithoutCode()
    {
        var factory = Factory();
        var local = ApiClients.CreateFrom(factory, ThisComputer);
        using var check = await local.PostAsJsonAsync("/api/installation/check", new InstallationCheck(null), TestContext.CancellationToken);
        var grant = (await check.Content.ReadFromJsonAsync<InstallationGrant>(Json, TestContext.CancellationToken))!;
        local.DefaultRequestHeaders.Add(InstallationAccess.Header, grant.Token);

        using var external = await local.PutAsJsonAsync("/api/installation", new InstallationInput(Mode: ServerMode.External), Json, TestContext.CancellationToken);
        using var done = await local.PostAsJsonAsync("/api/setup", new SetupInput(null, "admin", "admin-password", FastSignIn: true), TestContext.CancellationToken);
        var token = await factory.Services.GetRequiredService<FastSignIn>().IssueAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, external.StatusCode, await external.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.OK, done.StatusCode, await done.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsNull(token, "No start link on an External server, whatever the setting says");
    }

    [TestMethod]
    public async Task TheStartSettingsAreServerSettings()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);
        admin.DefaultRequestHeaders.Add(ApiClients.AddressHeader, ThisComputer);

        using var saved = await admin.PatchAsJsonAsync("/api/settings?level=server",
            new SettingChanges(new Dictionary<string, JsonNode?> { ["openBrowser"] = false, ["fastSignIn"] = true }), Json, TestContext.CancellationToken);
        using var tenant = await admin.PatchAsJsonAsync("/api/settings?level=tenant",
            new SettingChanges(new Dictionary<string, JsonNode?> { ["openBrowser"] = true }), Json, TestContext.CancellationToken);
        var settings = File.ReadAllText(_library.SettingsPath);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(TestContext.CancellationToken));
        StringAssert.Contains(settings, "\"openBrowser\": false");
        StringAssert.Contains(settings, "\"fastSignIn\": true");
        Assert.AreEqual(HttpStatusCode.BadRequest, tenant.StatusCode, "Only the server level sets them");
    }

    [TestMethod]
    [DataRow(new[] { "--urls", "http://localhost:5080" }, false, new[] { "--urls", "http://localhost:5080" })]
    [DataRow(new[] { "--no-browser", "--urls", "http://localhost:5080" }, true, new[] { "--urls", "http://localhost:5080" })]
    [DataRow(new[] { "--Sfe:Root=D:\\Media", "--NO-BROWSER" }, true, new[] { "--Sfe:Root=D:\\Media" })]
    public void NoBrowserIsTakenOutOfTheArguments(string[] args, bool expected, string[] rest)
    {
        var left = BrowserLauncher.TakeNoBrowser(args, out var noBrowser);

        Assert.AreEqual(expected, noBrowser);
        CollectionAssert.AreEqual(rest, left);
    }

    [TestMethod]
    [DataRow(new[] { "--urls", "http://localhost:5080" }, false, new[] { "--urls", "http://localhost:5080" })]
    [DataRow(new[] { "--no-logo", "--no-browser" }, true, new[] { "--no-browser" })]
    [DataRow(new[] { "/NO-LOGO", "--Sfe:Root=D:\\Media" }, true, new[] { "--Sfe:Root=D:\\Media" })]
    public void NoLogoIsTakenOutOfTheArguments(string[] args, bool expected, string[] rest)
    {
        var left = StartupLogo.TakeNoLogo(args, out var noLogo);

        Assert.AreEqual(expected, noLogo);
        CollectionAssert.AreEqual(rest, left);
    }

    [TestMethod]
    public void TheLogoIsAsciiWithTheVersion()
    {
        var logo = StartupLogo.Text("1.2.3");

        StringAssert.Contains(logo, "Simple File Explorer 1.2.3");
        Assert.IsTrue(logo.All(c => c < 128), "Any console code page shows it");
    }

    [TestMethod]
    [DataRow(new[] { "--help" }, true)]
    [DataRow(new[] { "--urls", "http://localhost:5080", "-?" }, true)]
    [DataRow(new[] { "/?" }, true)]
    [DataRow(new[] { "-H" }, true)]
    [DataRow(new[] { "--Sfe:Root=D:\\help" }, false)]
    public void HelpIsAskedWithTheUsualSwitches(string[] args, bool expected) =>
        Assert.AreEqual(expected, CommandLine.AsksForHelp(args));

    [TestMethod]
    public void TheHelpListsEverySwitchWithinEightyColumns()
    {
        var help = CommandLine.HelpText("1.2.3");

        foreach (var option in new[] { BrowserLauncher.NoBrowserArgument, StartupLogo.NoLogoArgument, "--help",
                                       WindowsServiceSetup.InstallArgument, WindowsServiceSetup.UninstallArgument, "--urls" })
            StringAssert.Contains(help, option);
        StringAssert.Contains(help, "Simple File Explorer 1.2.3");
        foreach (var line in help.Split('\n'))
            Assert.IsTrue(line.TrimEnd('\r').Length <= 80, $"Too long for a console: {line}");
        Assert.IsTrue(help.All(c => c < 128), "Any console code page shows it");
    }

    /// <summary>A browser on <paramref name="address"/>: keeps its cookie, doesn't follow redirects.</summary>
    private static HttpClient Browser(WebApplicationFactory<Program> factory, string address)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(RequestGuard.Header, "1");
        client.DefaultRequestHeaders.Add(ApiClients.AddressHeader, address);
        return client;
    }

    private async Task<HttpClient> SignInFromAsync(WebApplicationFactory<Program> factory, string address)
    {
        var client = ApiClients.CreateFrom(factory, address);
        using var response = await client.PostAsJsonAsync("/api/session/login", new SignInRequest(ApiClients.AdminName, ApiClients.AdminPassword), TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }

    /// <summary>A second install, for a test comparing two.</summary>
    private TestLibrary OtherLibrary()
    {
        var library = new TestLibrary();
        _disposables.Insert(0, library);
        return library;
    }

    /// <summary>A Local server (what these conveniences are for), unless <paramref name="configure"/> sets another mode.</summary>
    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null, TimeProvider? time = null, TestLibrary? library = null)
    {
        var factory = ApiClients.CreateFactory(library ?? _library, settings =>
        {
            settings.Remove(ServerModes.Key);
            configure?.Invoke(settings);
        });
        if (time is not null)
            factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton(time)));
        _disposables.Add(factory);
        return factory;
    }

    private sealed class ShiftedTime : TimeProvider
    {
        public TimeSpan Offset { get; set; }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
    }
}
