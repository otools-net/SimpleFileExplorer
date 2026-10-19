using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Sfe.Api;
using Sfe.Api.Endpoints;
using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Setting SFE up without editing a file: the setup code the server makes itself (<see cref="GeneratedSetupCode"/>) and
/// the installation page (<see cref="InstallationSettings"/>, its routes), through HTTP.
/// </summary>
[TestClass]
public sealed class InstallationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    private string CodeFile => Path.Combine(_library.BasePath, "setup-code.txt");
    private string InstallationFile => Path.Combine(_library.BasePath, "installation.json");

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
    public async Task WithoutAConfiguredCodeTheServerMakesOneAndAcceptsItAsRetyped()
    {
        var factory = Factory(settings => settings["Sfe:Setup:Code"] = "");
        var client = ApiClients.Create(factory);

        var setup = await GetAsync<SetupInfo>(client, "/api/setup");
        var code = (await File.ReadAllTextAsync(CodeFile, TestContext.CancellationToken)).Trim();
        var again = await GetAsync<SetupInfo>(client, "/api/setup");
        using var wrong = await client.PostAsJsonAsync("/api/setup", new SetupInput("ABCD-EFGH-JKMN-PQRS", "admin", "admin-password"), TestContext.CancellationToken);
        // Retyped from the console: lower case, without the dashes.
        using var done = await client.PostAsJsonAsync("/api/setup", new SetupInput(code.Replace("-", "").ToLowerInvariant(), "admin", "admin-password"),
            TestContext.CancellationToken);

        Assert.IsTrue(setup.CodeConfigured);
        Assert.IsTrue(setup.CodeGenerated);
        Assert.IsNotNull(setup.CodeFile);
        Assert.MatchesRegex("^[A-Z2-9]{4}(-[A-Z2-9]{4}){3}$", code);
        Assert.AreEqual(code, (await File.ReadAllTextAsync(CodeFile, TestContext.CancellationToken)).Trim(), "The code is made once");
        Assert.IsTrue(again.CodeGenerated);
        Assert.AreEqual(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, done.StatusCode, await done.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(File.Exists(CodeFile), "Kept for the update page");
    }

    [TestMethod]
    public async Task AConfiguredCodeWinsAndNoFileIsMade()
    {
        var client = ApiClients.Create(Factory());

        var setup = await GetAsync<SetupInfo>(client, "/api/setup");

        Assert.IsTrue(setup.CodeConfigured);
        Assert.IsFalse(setup.CodeGenerated);
        Assert.IsNull(setup.CodeFile);
        Assert.IsFalse(File.Exists(CodeFile));
    }

    [TestMethod]
    public async Task OnceSetUpNoCodeIsMade()
    {
        var factory = Factory();
        await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var later = Factory(settings => settings["Sfe:Setup:Code"] = "");

        var setup = await GetAsync<SetupInfo>(ApiClients.Create(later), "/api/setup");

        Assert.IsFalse(setup.Required);
        Assert.IsFalse(File.Exists(CodeFile), "Nothing needs a code until the next update");
    }

    [TestMethod]
    public void GeneratedCodesAreComparedWithoutDashesSpacesOrCase()
    {
        var generated = new Sfe.Core.Options.SetupOptions { Code = "AB2C-DE3F-GH4J-KM5N" };
        typeof(Sfe.Core.Options.SetupOptions).GetProperty(nameof(generated.CodeIsGenerated))!.SetValue(generated, true);
        var configured = new Sfe.Core.Options.SetupOptions { Code = "Some-Configured-Code" };

        Assert.IsTrue(generated.Matches("ab2c de3f gh4j km5n"));
        Assert.IsTrue(generated.Matches("AB2CDE3FGH4JKM5N"));
        Assert.IsFalse(generated.Matches("AB2CDE3FGH4JKM5"));
        Assert.IsTrue(configured.Matches(" Some-Configured-Code "));
        Assert.IsFalse(configured.Matches("some-configured-code"), "A configured code is compared exactly");
        Assert.IsFalse(configured.Matches("SomeConfiguredCode"));
    }

    [TestMethod]
    public async Task TheInstallationRoutesNeedTheSetupCode()
    {
        var client = ApiClients.Create(Factory());

        using var wrong = await client.PostAsJsonAsync("/api/installation/check", new InstallationCheck("not-the-setup-code"), TestContext.CancellationToken);
        using var noToken = await client.GetAsync("/api/installation", TestContext.CancellationToken);
        using var noTokenFolders = await client.GetAsync("/api/installation/folders", TestContext.CancellationToken);
        using var noTokenSave = await client.PutAsJsonAsync("/api/installation", new InstallationInput(Port: 6000), TestContext.CancellationToken);
        client.DefaultRequestHeaders.Add(InstallationAccess.Header, "forged-token");
        using var forged = await client.GetAsync("/api/installation", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, wrong.StatusCode);
        StringAssert.Contains(await wrong.Content.ReadAsStringAsync(TestContext.CancellationToken), "code");
        Assert.AreEqual(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, noTokenFolders.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, noTokenSave.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.IsFalse(File.Exists(InstallationFile));
    }

    [TestMethod]
    public async Task TheCodeCheckIsRateLimited()
    {
        var client = ApiClients.Create(Factory(settings => settings["Sfe:Security:SignInsPerMinute"] = "2"));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.PostAsJsonAsync("/api/installation/check", new InstallationCheck("not-the-setup-code"), TestContext.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.AreEqual(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [TestMethod]
    public async Task ThePageWorksBeforeTheSetupAndPicksTheLibraryFolder()
    {
        var factory = Factory(settings => settings.Remove("Sfe:Root"));
        var visitor = ApiClients.Create(factory);
        var grant = await GrantAsync(visitor);
        var top = await GetAsync<ServerFolders>(visitor, "/api/installation/folders");
        var media = await GetAsync<ServerFolders>(visitor, "/api/installation/folders?path=" + Uri.EscapeDataString(_library.Root));

        using var missing = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(Root: Path.Combine(_library.Root, "Nowhere")),
            TestContext.CancellationToken);
        using var saved = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(Root: _library.FullPath("Media")), TestContext.CancellationToken);
        var result = (await saved.Content.ReadFromJsonAsync<InstallationSaved>(Json, TestContext.CancellationToken))!;
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var listing = await GetAsync<JsonObject>(admin, "/api/browse");
        var file = JsonNode.Parse(await File.ReadAllTextAsync(InstallationFile, TestContext.CancellationToken))!;

        Assert.IsFalse(grant.Settings.Root.Locked, "Nothing but the installation file sets the folder here");
        Assert.IsNotEmpty(top.Folders, "The drives, or /");
        Assert.IsNull(top.Path);
        CollectionAssert.IsSubsetOf(new[] { "ChildDir", "Media", "SpecialDir" }, media.Folders.Select(f => f.Name).ToList());
        Assert.IsFalse(media.Folders.Any(f => f.Name == "RFile1.txt"), "Folders only");
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        StringAssert.Contains(await missing.Content.ReadAsStringAsync(TestContext.CancellationToken), "root");
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(result.Settings.RootExists);
        Assert.IsFalse(result.RestartRequired);
        StringAssert.StartsWith(file["Sfe"]!["Root"]!.GetValue<string>(), _library.FullPath("Media"));
        CollectionAssert.Contains(listing["items"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()).ToList(), "photo.jpg",
            "The library started again on the new folder, without a restart");
    }

    [TestMethod]
    public async Task ValuesSetByTheServersEnvironmentAreLocked()
    {
        // The tests' own settings come after the installation file, as environment variables would.
        var visitor = ApiClients.Create(Factory());
        var grant = await GrantAsync(visitor);

        using var refused = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(Root: _library.FullPath("Media")), TestContext.CancellationToken);

        Assert.IsTrue(grant.Settings.Root.Locked);
        Assert.IsNotNull(grant.Settings.Root.LockedBy);
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        StringAssert.Contains(await refused.Content.ReadAsStringAsync(TestContext.CancellationToken), "root");
        Assert.IsFalse(File.Exists(InstallationFile));
    }

    [TestMethod]
    public async Task TheAddressAppliesAfterARestartAndFfmpegIsChecked()
    {
        var visitor = ApiClients.Create(Factory());
        await GrantAsync(visitor);

        using var badPort = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(Port: 70000), TestContext.CancellationToken);
        using var noFfmpeg = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(FfmpegPath: _library.Root), TestContext.CancellationToken);
        using var saved = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(Network: true, Port: 5181), TestContext.CancellationToken);
        var result = (await saved.Content.ReadFromJsonAsync<InstallationSaved>(Json, TestContext.CancellationToken))!;
        var file = JsonNode.Parse(await File.ReadAllTextAsync(InstallationFile, TestContext.CancellationToken))!;

        Assert.AreEqual(HttpStatusCode.BadRequest, badPort.StatusCode);
        StringAssert.Contains(await badPort.Content.ReadAsStringAsync(TestContext.CancellationToken), "port");
        Assert.AreEqual(HttpStatusCode.BadRequest, noFfmpeg.StatusCode);
        StringAssert.Contains(await noFfmpeg.Content.ReadAsStringAsync(TestContext.CancellationToken), "ffmpegPath");
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual("http://*:5181", file["Urls"]!.GetValue<string>());
        Assert.IsNull(file["Sfe"], "Only what changed is written");
        Assert.IsTrue(result.RestartRequired);
        Assert.IsTrue(result.Settings.Network);
        Assert.AreEqual(5181, result.Settings.Port);
    }

    [TestMethod]
    public async Task AMissingLibraryFolderIsSaidAsSuch()
    {
        var factory = Factory(settings => settings["Sfe:Root"] = Path.Combine(_library.BasePath, "unplugged drive"));
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        using var root = await admin.GetAsync("/api/browse", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, root.StatusCode);
        StringAssert.Contains(await root.Content.ReadAsStringAsync(TestContext.CancellationToken), LibraryEndpoints.LibraryRootMissingType);
    }

    [TestMethod]
    [DataRow("http://*:5080", 5080, true)]
    [DataRow("http://localhost:5080", 5080, false)]
    [DataRow("http://127.0.0.1:8080;https://*:8443", 8080, false)]
    [DataRow("http://+:80", 80, true)]
    [DataRow("http://0.0.0.0:5000", 5000, true)]
    [DataRow("http://[::1]:5001", 5001, false)]
    [DataRow("http://nas", 80, true)]
    public void ListeningAddressesAreRead(string urls, int port, bool network) =>
        Assert.AreEqual((port, network), InstallationSettings.ParseUrls(urls));

    [TestMethod]
    [DataRow("http://[::]:5080", "http://localhost:5080/")]
    [DataRow("http://0.0.0.0:5080", "http://localhost:5080/")]
    [DataRow("http://localhost:5080", "http://localhost:5080/")]
    [DataRow("https://nas:443", "https://nas/")]
    [DataRow(null, null)]
    public void TheBrowserOpensAnAddressOfThisComputer(string? address, string? url) =>
        Assert.AreEqual(url, BrowserLauncher.BrowserUrl(address));

    [TestMethod]
    public async Task OnceSetUpTheCodeAloneOpensNothing()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var groups = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups");
        var spaceAdmins = groups.Single(g => g.BuiltIn == BuiltInGroup.Administrators);
        using (var created = await admin.PostAsJsonAsync("/api/access/users",
                   new UserInput("spaceadmin", Password: "spaceadmin-password", GroupIds: [spaceAdmins.Id]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync(TestContext.CancellationToken));
        var spaceAdmin = await ApiClients.SignInAsync(factory, "spaceadmin", "spaceadmin-password", TestContext.CancellationToken);
        var visitor = ApiClients.Create(factory);

        using var anonymous = await visitor.PostAsJsonAsync("/api/installation/check", new InstallationCheck(TestLibrary.SetupCode), TestContext.CancellationToken);
        using var notServerAdmin = await spaceAdmin.PostAsJsonAsync("/api/installation/check", new InstallationCheck(TestLibrary.SetupCode), TestContext.CancellationToken);
        // A token given to the server administrator does not work for others.
        var grant = await GrantAsync(admin);
        visitor.DefaultRequestHeaders.Add(InstallationAccess.Header, grant.Token);
        spaceAdmin.DefaultRequestHeaders.Add(InstallationAccess.Header, grant.Token);
        using var visitorSave = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(Port: 6000), TestContext.CancellationToken);
        using var visitorFolders = await visitor.GetAsync("/api/installation/folders", TestContext.CancellationToken);
        using var spaceAdminGet = await spaceAdmin.GetAsync("/api/installation", TestContext.CancellationToken);
        using var adminGet = await admin.GetAsync("/api/installation", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, notServerAdmin.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, visitorSave.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, visitorFolders.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, spaceAdminGet.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, adminGet.StatusCode);
        Assert.IsFalse(File.Exists(InstallationFile));
    }

    [TestMethod]
    public async Task FfmpegMustBeFfmpegAndNotInALibrary()
    {
        var visitor = ApiClients.Create(Factory());
        await GrantAsync(visitor);
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        // Anyone who can add files to the library would choose the program SFE runs.
        var planted = _library.AddFile($"Tools/{name}", 10);
        var other = Path.Combine(_library.BasePath, "programs", OperatingSystem.IsWindows() ? "evil.exe" : "evil");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        await File.WriteAllTextAsync(other, "x", TestContext.CancellationToken);

        using var inLibrary = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(FfmpegPath: planted), TestContext.CancellationToken);
        using var folderInLibrary = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(FfmpegPath: _library.FullPath("Tools")),
            TestContext.CancellationToken);
        using var notFfmpeg = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(FfmpegPath: other), TestContext.CancellationToken);

        foreach (var (response, expected) in new[] { (inLibrary, "library folder"), (folderInLibrary, "library folder"), (notFfmpeg, "not ffmpeg") })
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(TestContext.CancellationToken), expected);
        }

        Assert.IsFalse(File.Exists(InstallationFile));
    }

    [TestMethod]
    public async Task WhereTheCodeIsKeptIsToldOnlyWhileTheSetupNeedsIt()
    {
        var factory = Factory(settings => settings["Sfe:Setup:Code"] = "");
        var visitor = ApiClients.Create(factory);
        var before = await GetAsync<SetupInfo>(visitor, "/api/setup");
        var code = (await File.ReadAllTextAsync(CodeFile, TestContext.CancellationToken)).Trim();
        using (var done = await visitor.PostAsJsonAsync("/api/setup", new SetupInput(code, "admin", "admin-password"), TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, done.StatusCode);

        var after = await GetAsync<SetupInfo>(ApiClients.Create(factory), "/api/setup");
        var migration = await GetAsync<MigrationInfo>(ApiClients.Create(factory), "/api/migration");

        Assert.IsNotNull(before.CodeFile);
        Assert.IsNull(after.CodeFile);
        Assert.IsNull(migration.CodeFile);
    }

    [TestMethod]
    public async Task TheUpdateTo147RenewsAGeneratedCode()
    {
        var generated = new GeneratedSetupCode(CodeFile, Microsoft.Extensions.Logging.Abstractions.NullLogger<GeneratedSetupCode>.Instance);
        var old = generated.Ensure();
        var notes = new List<string>();
        var step = new Sfe.Core.Upgrades.Steps.V1_47_0.RenewSetupCode();
        var context = new Sfe.Core.Upgrades.Steps.UpgradeContext([], notes.Add, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, generated);

        await step.RunAsync(context, TestContext.CancellationToken);
        Assert.IsFalse(File.Exists(CodeFile), "Forgotten until needed");
        await step.RunAsync(context, TestContext.CancellationToken);
        var renewed = generated.Ensure();

        Assert.HasCount(1, notes, "Idempotent: nothing to renew the second time");
        Assert.AreNotEqual(old, renewed);
        Assert.IsTrue(Sfe.Core.Upgrades.Steps.UpgradeSteps.Due(new(1, 46, 0), new(1, 47, 0)).Any(s => s is Sfe.Core.Upgrades.Steps.V1_47_0.RenewSetupCode),
            "Runs for installs of 1.46.0");
    }

    [TestMethod]
    public async Task TheModeIsChosenAtTheSetupAndLocalOnlyFromThisComputer()
    {
        var factory = Factory();
        var remote = ApiClients.Create(factory);
        var local = ApiClients.CreateFrom(factory, "127.0.0.1");
        var grant = await GrantAsync(remote);
        var localGrant = await GrantAsync(local);

        using var remoteLocal = await remote.PutAsJsonAsync("/api/installation", new InstallationInput(Mode: ServerMode.Local), Json, TestContext.CancellationToken);
        using var external = await remote.PutAsJsonAsync("/api/installation", new InstallationInput(Mode: ServerMode.External), Json, TestContext.CancellationToken);
        var afterExternal = (await external.Content.ReadFromJsonAsync<InstallationSaved>(Json, TestContext.CancellationToken))!;
        // Before the setup is done, whoever sets it up may still change their mind.
        using var back = await local.PutAsJsonAsync("/api/installation", new InstallationInput(Mode: ServerMode.Local), Json, TestContext.CancellationToken);
        var file = JsonNode.Parse(await File.ReadAllTextAsync(InstallationFile, TestContext.CancellationToken))!;

        Assert.IsNull(grant.Settings.Mode.Value, "Not chosen yet");
        Assert.IsFalse(grant.Settings.Mode.Locked);
        Assert.AreEqual(ServerMode.Local, grant.Settings.ModeInEffect, "No mode configured = Local, as installs older than the modes");
        Assert.IsFalse(grant.Settings.OnThisComputer);
        Assert.IsTrue(localGrant.Settings.OnThisComputer);
        Assert.AreEqual(HttpStatusCode.BadRequest, remoteLocal.StatusCode);
        StringAssert.Contains(await remoteLocal.Content.ReadAsStringAsync(TestContext.CancellationToken), "mode");
        Assert.AreEqual(HttpStatusCode.OK, external.StatusCode, await external.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(ServerMode.External, afterExternal.Settings.ModeInEffect);
        Assert.AreEqual(HttpStatusCode.OK, back.StatusCode, await back.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual("Local", file["Sfe"]!["Mode"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task OnceSetUpExternalNeverGoesBackToLocalFromTheWeb()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        admin.DefaultRequestHeaders.Add(ApiClients.AddressHeader, "127.0.0.1");
        await GrantAsync(admin);

        using var external = await admin.PutAsJsonAsync("/api/installation", new InstallationInput(Mode: ServerMode.External), Json, TestContext.CancellationToken);
        using var local = await admin.PutAsJsonAsync("/api/installation", new InstallationInput(Mode: ServerMode.Local), Json, TestContext.CancellationToken);
        var file = JsonNode.Parse(await File.ReadAllTextAsync(InstallationFile, TestContext.CancellationToken))!;

        Assert.AreEqual(HttpStatusCode.OK, external.StatusCode, await external.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.BadRequest, local.StatusCode, "Even from this computer");
        StringAssert.Contains(await local.Content.ReadAsStringAsync(TestContext.CancellationToken), "External mode");
        Assert.AreEqual("External", file["Sfe"]!["Mode"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task AModeSetByTheEnvironmentIsLocked()
    {
        var visitor = ApiClients.CreateFrom(Factory(settings => settings["Sfe:Mode"] = "external"), "127.0.0.1");
        var grant = await GrantAsync(visitor);

        using var refused = await visitor.PutAsJsonAsync("/api/installation", new InstallationInput(Mode: ServerMode.Local), Json, TestContext.CancellationToken);

        Assert.IsTrue(grant.Settings.Mode.Locked);
        Assert.AreEqual(ServerMode.External, grant.Settings.ModeInEffect);
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.IsFalse(File.Exists(InstallationFile));
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", null)]
    [DataRow("local", ServerMode.Local)]
    [DataRow(" External ", ServerMode.External)]
    [DataRow("Externl", ServerMode.External)]
    [DataRow("0", ServerMode.External)]
    public void AnUnknownModeCountsAsExternal(string? configured, ServerMode? mode)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ServerModes.Key] = configured }).Build();

        Assert.AreEqual(mode, ServerModes.Configured(configuration));
        Assert.AreEqual(mode ?? ServerMode.Local, ServerModes.InEffect(configuration));
    }

    private async Task<InstallationGrant> GrantAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/installation/check", new InstallationCheck(TestLibrary.SetupCode), TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        var grant = (await response.Content.ReadFromJsonAsync<InstallationGrant>(Json, TestContext.CancellationToken))!;
        client.DefaultRequestHeaders.Remove(InstallationAccess.Header);
        client.DefaultRequestHeaders.Add(InstallationAccess.Header, grant.Token);
        return grant;
    }

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        return factory;
    }
}
