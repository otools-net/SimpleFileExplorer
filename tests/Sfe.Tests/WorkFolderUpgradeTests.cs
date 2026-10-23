using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Options;
using Sfe.Core.Settings;
using Sfe.Core.Upgrades.Steps;
using Sfe.Core.Upgrades.Steps.V1_82_0;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The work folder on the server by default since 1.82.0, and the step keeping the libraries of earlier installs on
/// the library's (<see cref="KeepWorkFolderInLibraries"/>, <see cref="SettingLayers.KeepPreviousDefaultAsync"/>).
/// </summary>
[TestClass]
public sealed class WorkFolderUpgradeTests
{
    private const string Key = KeepWorkFolderInLibraries.Key;

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];
    private readonly List<string> _notes = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

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
    public async Task ANewInstallKeepsItsWorkFilesOnTheServer()
    {
        var factory = await InstallAsync();

        var work = await WorkFolderAsync(factory, await DefaultTenantAsync(factory));

        Assert.AreEqual(WorkFolderLocation.Server, work.Location);
        Assert.AreEqual(Path.Combine(_library.ThumbnailPath, WorkFolder.ServerFolderName, "trash"), work.Get("trash"));
        Assert.IsFalse(Directory.Exists(_library.FullPath(WorkFolder.Name)));
    }

    [TestMethod]
    public async Task TheLibrariesOfAnEarlierInstallKeepTheirWorkFolderInTheLibrary()
    {
        var factory = await InstallAsync();
        var access = factory.Services.GetRequiredService<AccessService>();
        var first = await DefaultTenantAsync(factory);
        var other = await CreateTenantAsync(access, "Other", "Media");
        var chosen = await CreateTenantAsync(access, "Chosen", "ChildDir");
        var rows = factory.Services.GetRequiredService<SettingLayers>().Rows;
        await rows.SaveTenantAsync(chosen, new Dictionary<string, StoredSetting> { [Key] = new(JsonValue.Create("Server"), false) }, Token);

        await RunAsync(factory);
        var later = await CreateTenantAsync(access, "Later", "RepeatedDir");
        await RunAsync(factory);

        Assert.AreEqual(WorkFolderLocation.Library, (await WorkFolderAsync(factory, first)).Location);
        Assert.AreEqual(WorkFolderLocation.Library, (await WorkFolderAsync(factory, other)).Location);
        Assert.AreEqual(WorkFolderLocation.Server, (await WorkFolderAsync(factory, chosen)).Location, "A library's own choice stays");
        Assert.AreEqual(WorkFolderLocation.Server, (await WorkFolderAsync(factory, later)).Location, "A library created after the update gets the new default");
        Assert.IsFalse(rows.ForTenant(later).ContainsKey(Key));
        Assert.IsTrue(File.Exists(Path.Combine(_library.BasePath, KeepWorkFolderInLibraries.MarkerName)));
        CollectionAssert.AreEqual(new[] { "2 libraries keep their work folder in the library (Files › Where SFE keeps its work files)." }, _notes);
    }

    [TestMethod]
    public async Task AWorkFolderSetByTheConfigurationIsLeftAlone()
    {
        var factory = await InstallAsync(s => s["Sfe:Files:WorkFolder"] = "Server");

        await RunAsync(factory);

        Assert.IsEmpty(factory.Services.GetRequiredService<SettingLayers>().Rows.ForTenant(await DefaultTenantAsync(factory)));
        Assert.AreEqual(WorkFolderLocation.Server, (await WorkFolderAsync(factory, await DefaultTenantAsync(factory))).Location);
        Assert.IsEmpty(_notes);
    }

    [TestMethod]
    public async Task AWorkFolderSetByTheServerIsLeftAlone()
    {
        var saved = await InstallAsync();
        var layers = saved.Services.GetRequiredService<SettingLayers>();
        Assert.IsTrue(await layers.SaveServerValueAsync(Key, JsonValue.Create("Library"), Token));
        await RunAsync(saved);

        Assert.IsEmpty(layers.Rows.ForTenant(await DefaultTenantAsync(saved)));
        Assert.AreEqual(WorkFolderLocation.Library, (await WorkFolderAsync(saved, await DefaultTenantAsync(saved))).Location);
    }

    [TestMethod]
    public async Task ADefaultLockedByTheServerKeepsThePreviousValueAsTheServers()
    {
        var factory = await InstallAsync();
        var layers = factory.Services.GetRequiredService<SettingLayers>();
        await layers.Server.SaveLayerAsync(new Dictionary<string, JsonNode?>(), [Key], Token);
        layers.NotifyChanged();

        await RunAsync(factory);

        Assert.IsTrue(layers.Server.Locked.Contains(Key));
        Assert.IsTrue(layers.Server.Values.ContainsKey(Key));
        Assert.IsEmpty(layers.Rows.ForTenant(await DefaultTenantAsync(factory)));
        Assert.AreEqual(WorkFolderLocation.Library, (await WorkFolderAsync(factory, await DefaultTenantAsync(factory))).Location);
    }

    private async Task<WebApplicationFactory<Program>> InstallAsync(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        await ApiClients.SetUpAsync(factory, Token);
        return factory;
    }

    private async Task RunAsync(WebApplicationFactory<Program> factory)
    {
        var tenants = await factory.Services.GetRequiredService<AccessService>().GetTenantsAsync(Token);
        var context = new UpgradeContext([], _notes.Add, NullLogger.Instance,
            Settings: factory.Services.GetRequiredService<SettingLayers>(), TenantIds: [.. tenants.Select(t => t.Id)]);
        await new KeepWorkFolderInLibraries().RunAsync(context, Token);
    }

    private async Task<int> DefaultTenantAsync(WebApplicationFactory<Program> factory) =>
        (await factory.Services.GetRequiredService<AccessService>().GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;

    private async Task<int> CreateTenantAsync(AccessService access, string name, string root)
    {
        var created = await access.CreateTenantAsync(new TenantInput(name, Slug: name.ToLowerInvariant()), _library.FullPath(root), Token);
        Assert.AreEqual(AccessStatus.Ok, created.Status, created.Problem);
        return created.Value!.Id;
    }

    private async Task<WorkFolder> WorkFolderAsync(WebApplicationFactory<Program> factory, int tenantId)
    {
        var host = await factory.Services.GetRequiredService<TenantRegistry>().GetAsync(tenantId, Token);
        return host!.Services.GetRequiredService<WorkFolder>();
    }
}
