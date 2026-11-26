using System.Net;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Properties' Access section (<see cref="ItemProperties.Access"/>): what the asker may do with an item, why not, the
/// folder a rule is set on and what the disk allows (agents/docs/features/item-access.md).
/// </summary>
[TestClass]
public sealed class ItemAccessRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly ItemAction[] Changes = [ItemAction.SaveOver, ItemAction.Rename, ItemAction.Move];

    private readonly TestLibrary _library = new();
    private WebApplicationFactory<Program>? _factory;
    private string? _denied;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestCleanup]
    public void Cleanup()
    {
        new FileInfo(_library.FullPath("Media/photo.jpg")).IsReadOnly = false;
        if (_denied is not null && OperatingSystem.IsWindows())
            ChangeRule(_denied, add: false);
        else if (_denied is not null)
            File.SetUnixFileMode(_denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AdministratorsMayDoEverything()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        var access = await AccessAsync(admin, "Media/photo.jpg");

        CollectionAssert.AreEqual(
            new[]
            {
                ItemAction.Open,
                ItemAction.Download,
                ItemAction.SaveOver,
                ItemAction.Rename,
                ItemAction.Move,
                ItemAction.Delete,
            },
            access.Actions.Select(a => a.Action).ToArray()
        );
        Assert.IsTrue(access.Actions.All(a => a.Allowed && a.Reason is null));
        Assert.IsNull(access.RuleFolder);
        Assert.IsNull(access.Disk);
    }

    [TestMethod]
    public async Task TheTopHasNothingToRenameMoveOrDelete()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);

        var access = await AccessAsync(admin, "");

        CollectionAssert.AreEqual(
            new[] { ItemAction.Open, ItemAction.Download, ItemAction.CreateInside },
            access.Actions.Select(a => a.Action).ToArray()
        );
    }

    [TestMethod]
    public async Task TheSpacesSwitchRefusesTheChanges()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);
        await SwitchOffFilesAsync();

        var access = await AccessAsync(admin, "Media/photo.jpg");

        Assert.IsTrue(Of(access, ItemAction.Open).Allowed);
        Assert.IsTrue(Of(access, ItemAction.Download).Allowed);
        foreach (var change in Changes)
            Assert.AreEqual(ReadOnlyReason.Space, Of(access, change).Reason, change.ToString());
    }

    [TestMethod]
    public async Task AFolderRuleIsToldWithTheFolderItIsSetOn()
    {
        var factory = Factory();
        await ApiClients.SetUpAsync(factory, Token);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var tenant = await DefaultTenantAsync();
        var editors = new GroupInput("Editors", Rights: [Rights.Browse, Rights.ViewFiles, Rights.RenameFiles]);
        var users = (await accounts.CreateGroupAsync(tenant.Id, editors, cancellationToken: Token)).Value!.Id;
        await accounts.CreateUserAsync(
            new UserInput("alice", Password: "alice-password-1", GroupIds: [users]),
            null,
            Token
        );
        await factory
            .Services.GetRequiredService<FolderRuleService>()
            .SetAsync(tenant.Id, "Media", FolderPrincipalRef.Group(users), FolderLevel.Read, null, Token);
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password-1", Token);

        var access = await AccessAsync(alice, "Media/photo.jpg");

        Assert.AreEqual("Media", access.RuleFolder);
        Assert.IsTrue(Of(access, ItemAction.Open).Allowed);
        Assert.AreEqual(ReadOnlyReason.Rule, Of(access, ItemAction.Rename).Reason);
        Assert.AreEqual(ReadOnlyReason.Rights, Of(access, ItemAction.Move).Reason, "No right to move anywhere");
        Assert.IsNull(access.Disk, "The disk takes changes");
    }

    [TestMethod]
    public async Task AFileTheDiskMarksReadOnlyCantBeSavedOver()
    {
        var admin = await ApiClients.SetUpAsync(Factory(), Token);
        new FileInfo(_library.FullPath("Media/photo.jpg")).IsReadOnly = true;

        var access = await AccessAsync(admin, "Media/photo.jpg");

        Assert.AreEqual(ReadOnlyReason.FileReadOnly, Of(access, ItemAction.SaveOver).Reason);
        Assert.IsTrue(Of(access, ItemAction.Rename).Allowed, "Its folder takes changes");
        Assert.IsNull(access.Disk);
    }

    [TestMethod]
    public async Task AFolderTheDiskRefusesTakesNothingInside()
    {
        if (!OperatingSystem.IsWindows() && Environment.UserName == "root")
            Assert.Inconclusive("root writes everywhere but on read-only mounts.");
        var admin = await ApiClients.SetUpAsync(Factory(), Token);
        _denied = _library.FullPath("ChildDir");
        if (OperatingSystem.IsWindows())
            ChangeRule(_denied, add: true);
        else
            File.SetUnixFileMode(_denied, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var access = await AccessAsync(admin, "ChildDir");

        Assert.AreEqual(ReadOnlyReason.DiskRefused, access.Disk);
        Assert.AreEqual(ReadOnlyReason.DiskRefused, Of(access, ItemAction.CreateInside).Reason);
        Assert.IsTrue(Of(access, ItemAction.Rename).Allowed, "Its parent takes changes");
    }

    private static ActionAccess Of(ItemAccess access, ItemAction action) =>
        access.Actions.Single(a => a.Action == action);

    private WebApplicationFactory<Program> Factory() => _factory = ApiClients.CreateFactory(_library);

    private async Task<TenantInfo> DefaultTenantAsync() =>
        (await _factory!.Services.GetRequiredService<AccessService>().GetTenantsAsync(Token)).Single(t => t.IsDefault);

    /// <summary>The space editor's <b>Files read-only</b> button.</summary>
    private async Task SwitchOffFilesAsync()
    {
        var tenant = await DefaultTenantAsync();
        string[] fileRights = [.. RightCatalog.TenantRights.Where(r => r.ChangesFiles).Select(r => r.Id)];
        var changed = await _factory!
            .Services.GetRequiredService<AccessService>()
            .UpdateTenantAsync(
                tenant.Id,
                new TenantInput(tenant.Name, tenant.Slug, tenant.Root, tenant.GuestAccess, false, fileRights),
                Token
            );
        Assert.AreEqual(AccessStatus.Ok, changed.Status);
    }

    private async Task<ItemAccess> AccessAsync(HttpClient client, string path)
    {
        var url = $"{V1}/files/properties?path={Uri.EscapeDataString(path)}";
        using var response = await client.GetAsync(url, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ItemProperties>(Json, Token))!.Access!;
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
