using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Previous versions through HTTP (<c>api/files/versions</c>). Eve may open, delete and create files (Media read only
/// for her); Cara may open and create files, not delete them; Bob may browse but not open files.
/// </summary>
[TestClass]
public sealed class VersionRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _eve = null!;
    private HttpClient _cara = null!;
    private HttpClient _bob = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients.CreateFactory(_library);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        var editors = await GroupAsync(
            accounts,
            tenant,
            "Editors",
            Rights.ViewFiles,
            Rights.Download,
            Rights.CreateFiles,
            Rights.DeleteFiles
        );
        var copiers = await GroupAsync(accounts, tenant, "Copiers", Rights.ViewFiles, Rights.CreateFiles);
        var browsers = await GroupAsync(accounts, tenant, "Browsers");
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        var readOnly = await rules.SetAsync(
            tenant,
            "Media",
            FolderPrincipalRef.Group(editors),
            FolderLevel.Read,
            null,
            Token
        );
        Assert.AreEqual(AccessStatus.Ok, readOnly.Status);
        _eve = await UserAsync(accounts, "eve", editors);
        _cara = await UserAsync(accounts, "cara", copiers);
        _bob = await UserAsync(accounts, "bob", browsers);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _bob?.Dispose();
        _cara?.Dispose();
        _eve?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task TheListSaysWhatTheCallerMayDoAsTheRightsAndRulesSay()
    {
        await EditedAsync("Docs/notes.txt");
        await EditedAsync("Media/notes.txt");

        var eve = await ListAsync(_eve, "Docs/notes.txt");
        var cara = await ListAsync(_cara, "Docs/notes.txt");
        var readOnly = await ListAsync(_eve, "Media/notes.txt");

        Assert.HasCount(1, eve.Items);
        Assert.IsTrue(eve.RestoreOver && eve.RestoreCopy && eve.Download);
        Assert.IsFalse(cara.RestoreOver, "Cara may not delete");
        Assert.IsTrue(cara.RestoreCopy);
        Assert.IsFalse(readOnly.RestoreOver || readOnly.RestoreCopy, "A read-only folder");
        using var bob = await _bob.GetAsync($"{ApiPaths.V1}/files/versions?path=Docs%2Fnotes.txt", Token);
        Assert.AreEqual(HttpStatusCode.Forbidden, bob.StatusCode, "Bob may not open files");
    }

    [TestMethod]
    public async Task AVersionIsOpenedAsTheFileIs()
    {
        var id = await EditedAsync("Docs/notes.txt");

        using var eve = await _eve.GetAsync($"{ApiPaths.V1}/files/versions/{id}/content", Token);
        using var download = await _cara.GetAsync($"{ApiPaths.V1}/files/versions/{id}/content?download=true", Token);
        using var missing = await _eve.GetAsync($"{ApiPaths.V1}/files/versions/{id + 100}/content", Token);

        Assert.AreEqual(HttpStatusCode.OK, eve.StatusCode);
        Assert.AreEqual("original", await eve.Content.ReadAsStringAsync(Token));
        Assert.AreEqual(HttpStatusCode.Forbidden, download.StatusCode, "Cara may not download");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [TestMethod]
    public async Task AVersionIsRestoredAsACopyOrOverWithTheRightsOfEach()
    {
        var id = await EditedAsync("Docs/notes.txt");
        var file = _library.FullPath("Docs/notes.txt");

        using var caraOver = await _cara.PostAsync($"{ApiPaths.V1}/files/versions/{id}/restore", null, Token);
        using var caraCopy = await _cara.PostAsync($"{ApiPaths.V1}/files/versions/{id}/copy", null, Token);
        Assert.AreEqual(HttpStatusCode.Forbidden, caraOver.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, caraCopy.StatusCode, await caraCopy.Content.ReadAsStringAsync(Token));
        var copy = (await caraCopy.Content.ReadFromJsonAsync<LibraryItem>(Json, Token))!;
        Assert.AreEqual("Docs/notes (2).txt", copy.Path);

        using var eveOver = await _eve.PostAsync($"{ApiPaths.V1}/files/versions/{id}/restore", null, Token);
        Assert.AreEqual(HttpStatusCode.OK, eveOver.StatusCode, await eveOver.Content.ReadAsStringAsync(Token));
        var restored = (await eveOver.Content.ReadFromJsonAsync<RestoredVersion>(Json, Token))!;
        Assert.AreEqual("original", File.ReadAllText(file));
        Assert.IsNotNull(restored.TrashId);
        var list = await ListAsync(_eve, "Docs/notes.txt");
        Assert.AreEqual(restored.TrashId, list.Items.Single().Id, "The content it had is the version now");
    }

    [TestMethod]
    public async Task ADeletedFileOfTheTrashIsNotAVersion()
    {
        WriteText("Docs/gone.txt", "gone");
        using var delete = await _admin.PostAsJsonAsync(
            $"{ApiPaths.V1}/files/delete",
            new DeleteRequest(["Docs/gone.txt"]),
            Json,
            Token
        );
        var deleted = (await delete.Content.ReadFromJsonAsync<DeleteResults>(Json, Token))!.Items.Single().TrashId;

        using var response = await _admin.PostAsync($"{ApiPaths.V1}/files/versions/{deleted}/copy", null, Token);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The file written then saved over by the administrator: the id of its previous version.</summary>
    private async Task<int> EditedAsync(string path)
    {
        WriteText(path, "original");
        var body = new SaveTextRequest("edited", TextCodec.Utf8, "lf", "session-1");
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{ApiPaths.V1}/files/text?path={Uri.EscapeDataString(path)}"
        )
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "*");
        using var response = await _admin.SendAsync(request, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<SavedText>(Json, Token))!.TrashId!.Value;
    }

    private async Task<FileVersions> ListAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(
            $"{ApiPaths.V1}/files/versions?path={Uri.EscapeDataString(path)}",
            Token
        );
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FileVersions>(Json, Token))!;
    }

    private async Task<int> GroupAsync(AccessService accounts, int tenant, string name, params string[] rights)
    {
        var group = await accounts.CreateGroupAsync(
            tenant,
            new GroupInput(name, Rights: [Rights.Browse, .. rights]),
            cancellationToken: Token
        );
        return group.Value!.Id;
    }

    private async Task<HttpClient> UserAsync(AccessService accounts, string name, int group)
    {
        var password = $"{name}-password-1";
        await accounts.CreateUserAsync(new UserInput(name, Password: password, GroupIds: [group]), null, Token);
        return await ApiClients.SignInAsync(_factory, name, password, Token);
    }

    private string WriteText(string relative, string text)
    {
        var full = _library.FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, Encoding.UTF8);
        return full;
    }
}
