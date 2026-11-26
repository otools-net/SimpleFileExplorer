using System.Net;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Why a folder listed is read-only for the asker (<see cref="DirectoryListing.ReadOnly"/>), the files the disk marks
/// read-only and the entries a folder rule makes read-only (agents/docs/features/item-access.md).
/// </summary>
[TestClass]
public sealed class ListingReadOnlyRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly string[] FileRights =
    [
        .. RightCatalog.TenantRights.Where(r => r.ChangesFiles).Select(r => r.Id),
    ];

    private readonly TestLibrary _library = new();
    private WebApplicationFactory<Program>? _factory;
    private string? _denied;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var file in Directory.EnumerateFiles(_library.Root, "*", SearchOption.AllDirectories))
            new FileInfo(file).IsReadOnly = false;
        if (_denied is not null && OperatingSystem.IsWindows())
            ChangeRule(_denied, add: false);
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AdministratorsMayChangeFilesEverywhere()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        var root = await BrowseAsync(admin, "");

        Assert.IsNull(root.ReadOnly);
        Assert.IsTrue(root.Items.All(i => i.ReadOnly is null), "No file is marked read-only");
    }

    [TestMethod]
    public async Task TheServersSwitchComesFirst()
    {
        var factory = Factory(settings => settings["Sfe:Rights:Disabled:0"] = RightCatalog.FileWriteSelector);
        var admin = await ApiClients.SetUpAsync(factory, Token);
        await SwitchOffFilesAsync();

        Assert.AreEqual(ReadOnlyReason.Server, (await BrowseAsync(admin, "")).ReadOnly);
    }

    [TestMethod]
    public async Task TheSpacesFilesReadOnlySwitchIsTold()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);
        await SwitchOffFilesAsync();

        Assert.AreEqual(ReadOnlyReason.Space, (await BrowseAsync(admin, "")).ReadOnly);
    }

    [TestMethod]
    public async Task VisitorsAndAccountsWithoutTheRightsAreTold()
    {
        var factory = Factory(settings => settings["Sfe:Security:AnonymousGuests"] = "true");
        await ApiClients.SetUpAsync(factory, Token);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = await DefaultTenantAsync();
        await accounts.SetGuestAccessAsync(tenant.Id, true, Token);
        var viewers = (
            await accounts.CreateGroupAsync(
                tenant.Id,
                new GroupInput("Viewers", Rights: [Rights.Browse, Rights.ViewFiles]),
                cancellationToken: Token
            )
        )
            .Value!
            .Id;
        await accounts.CreateUserAsync(
            new UserInput("bob", Password: "bob-password-1", GroupIds: [viewers]),
            null,
            Token
        );
        var bob = await ApiClients.SignInAsync(factory, "bob", "bob-password-1", Token);

        Assert.AreEqual(ReadOnlyReason.Visitor, (await BrowseAsync(ApiClients.Create(factory), "")).ReadOnly);
        Assert.AreEqual(ReadOnlyReason.Rights, (await BrowseAsync(bob, "")).ReadOnly);
    }

    [TestMethod]
    public async Task AFolderRuleMakesTheFolderAndItsEntryReadOnly()
    {
        var factory = Factory();
        await ApiClients.SetUpAsync(factory, Token);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = await DefaultTenantAsync();
        var users = (await accounts.GetBuiltInGroupAsync(tenant.Id, BuiltInGroup.Users, Token))!.Id;
        await accounts.CreateUserAsync(
            new UserInput("alice", Password: "alice-password-1", GroupIds: [users]),
            null,
            Token
        );
        await factory
            .Services.GetRequiredService<FolderRuleService>()
            .SetAsync(tenant.Id, "Media", FolderPrincipalRef.Group(users), FolderLevel.Read, null, Token);
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password-1", Token);

        var root = await BrowseAsync(alice, "");
        var media = await BrowseAsync(alice, "Media");

        Assert.IsNull(root.ReadOnly, "Users may edit pictures at the top");
        Assert.IsTrue(root.Access!.Children!["Media"].ReadOnly);
        Assert.IsFalse(root.Access.ReadOnly);
        Assert.AreEqual(ReadOnlyReason.Rule, media.ReadOnly);
    }

    [TestMethod]
    public async Task AFileTheDiskMarksReadOnlyIsMarked()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);
        new FileInfo(_library.FullPath("Media/photo.jpg")).IsReadOnly = true;

        var media = await BrowseAsync(admin, "Media");

        Assert.IsTrue(media.Items.Single(i => i.Name == "photo.jpg").ReadOnly);
        Assert.IsTrue(media.Items.Where(i => i.Name != "photo.jpg").All(i => i.ReadOnly is null));
        Assert.IsNull(media.ReadOnly, "A file's attribute says nothing of its folder");
    }

    [TestMethod]
    public async Task AFolderTheDiskRefusesIsTold()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Denying the server's account is tested with a Windows ACL.");
        var admin = await ApiClients.SetUpAsync(Factory(), Token);
        _denied = _library.FullPath("ChildDir");
        ChangeRule(_denied, add: true);

        Assert.AreEqual(ReadOnlyReason.DiskRefused, (await BrowseAsync(admin, "ChildDir")).ReadOnly);
        Assert.IsNull((await BrowseAsync(admin, "Media")).ReadOnly);
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? settings = null) =>
        _factory = ApiClients.CreateFactory(_library, settings);

    private async Task<TenantInfo> DefaultTenantAsync() =>
        (await _factory!.Services.GetRequiredService<AccessService>().GetTenantsAsync(Token)).Single(t => t.IsDefault);

    /// <summary>The space editor's <b>Files read-only</b> button.</summary>
    private async Task SwitchOffFilesAsync()
    {
        var tenant = await DefaultTenantAsync();
        var changed = await _factory!
            .Services.GetRequiredService<AccessService>()
            .UpdateTenantAsync(
                tenant.Id,
                new TenantInput(tenant.Name, tenant.Slug, tenant.Root, tenant.GuestAccess, false, FileRights),
                Token
            );
        Assert.AreEqual(AccessStatus.Ok, changed.Status);
    }

    private async Task<DirectoryListing> BrowseAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"{V1}/browse?path={Uri.EscapeDataString(path)}", Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<DirectoryListing>(Json, Token))!;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ChangeRule(string folder, bool add)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
            AccessControlType.Deny
        );
        if (add)
            security.AddAccessRule(rule);
        else
            security.RemoveAccessRule(rule);
        info.SetAccessControl(security);
    }
}
