using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Files;
using Sfe.Core.Settings;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Zips through HTTP. Alice is in Users (who may download) with SpecialDir hidden from them; Carol may only browse.
/// </summary>
[TestClass]
public sealed class ArchiveRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _carol = null!;
    private readonly List<string> _links = [];

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
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!;
        Assert.Contains(Rights.Download, users.Rights, "Members may download by default");
        var readers = (await accounts.CreateGroupAsync(tenant, new GroupInput("Readers", Rights: [Rights.Browse, Rights.ViewFiles]), cancellationToken: Token)).Value!;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("carol", Password: "carol-password-1", GroupIds: [readers.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(users.Id), FolderLevel.Hidden, null, Token)).Status);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
        _carol = await ApiClients.SignInAsync(_factory, "carol", "carol-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var link in _links)
            Directory.Delete(link);
        _carol?.Dispose();
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task SelectedItemsAreDownloadedAsAZipFromALinkThatWorksOnce()
    {
        var link = await LinkAsync(_alice, "ChildDir", "RFile1.txt");
        Assert.AreEqual("Root.zip", link.FileName);

        using var response = await _alice.GetAsync(link.Url, HttpCompletionOption.ResponseHeadersRead, Token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("Root.zip", response.Content.Headers.ContentDisposition?.FileNameStar);
        using var zip = await ZipAsync(response);
        CollectionAssert.AreEquivalent(new[] { "ChildDir/", "ChildDir/AFile1.txt", "ChildDir/AFile2", "RFile1.txt" }, zip.Entries.Select(e => e.FullName).ToArray());
        using var again = await _alice.GetAsync(link.Url, Token);
        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode, "Once");
    }

    [TestMethod]
    public async Task HiddenFoldersAreNotFoundAndLeftOutOfTheRoot()
    {
        using var hidden = await _alice.PostAsJsonAsync("/api/files/archive", new ArchiveRequest(["SpecialDir"]), Json, Token);
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        using var missing = await _alice.PostAsJsonAsync("/api/files/archive", new ArchiveRequest(["../RFile1.txt"]), Json, Token);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);

        var link = await LinkAsync(_alice, "");
        using var response = await _alice.GetAsync(link.Url, Token);
        using var zip = await ZipAsync(response);

        Assert.Contains("Root/Media/photo.jpg", zip.Entries.Select(e => e.FullName));
        Assert.IsFalse(zip.Entries.Any(e => e.FullName.Contains("SpecialDir") || e.FullName.Contains("SDEspañol")));
        Assert.IsNull(zip.GetEntry(Archives.LeftOutEntry), "A hidden folder is not even named");
    }

    [TestMethod]
    public async Task OnlyThoseWhoMayDownloadGetAZip()
    {
        using var refused = await _carol.PostAsJsonAsync("/api/files/archive", new ArchiveRequest(["ChildDir"]), Json, Token);
        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);

        // Someone else's link: as if it didn't exist (and it still works for its owner).
        var link = await LinkAsync(_alice, "ChildDir");
        using (var other = await _admin.GetAsync(link.Url, Token))
            Assert.AreEqual(HttpStatusCode.NotFound, other.StatusCode);
        using var own = await _alice.GetAsync(link.Url, Token);
        Assert.AreEqual(HttpStatusCode.OK, own.StatusCode);
    }

    [TestMethod]
    public async Task ALinkToTheServersFilesIsNeitherZippedNorFollowed()
    {
        Directory.CreateDirectory(_library.LogsPath);
        await File.WriteAllTextAsync(Path.Combine(_library.LogsPath, "server.log"), "secret", Token);
        if (!Link("ChildDir/Logs", _library.LogsPath))
            Assert.Inconclusive("Links can't be made here.");

        using var direct = await _admin.PostAsJsonAsync("/api/files/archive", new ArchiveRequest(["ChildDir/Logs"]), Json, Token);
        Assert.AreEqual(HttpStatusCode.NotFound, direct.StatusCode);
        var link = await LinkAsync(_admin, "ChildDir");
        using var response = await _admin.GetAsync(link.Url, Token);
        using var zip = await ZipAsync(response);

        Assert.IsFalse(zip.Entries.Any(e => e.FullName.Contains("server.log")));
    }

    [TestMethod]
    public async Task ACallerDownloadsTwoZipsAtOnce()
    {
        var links = _factory.Services.GetRequiredService<ArchiveLinks>();
        using var first = links.TryBegin("user:1");
        using var second = links.TryBegin("user:1");

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsNull(links.TryBegin("user:1"));
        Assert.IsFalse(links.HasRoom("user:1"));
        Assert.IsTrue(links.HasRoom("user:2"));
        first!.Dispose();
        using var third = links.TryBegin("user:1");
        Assert.IsNotNull(third);
    }

    [TestMethod]
    public async Task AZipLargerThanTheSpaceAllowsIsRefusedBeforeItStarts()
    {
        var settings = (await _admin.GetFromJsonAsync<SettingsView>("/api/settings", Json, Token))!;
        settings.Values.Files.MaxArchiveMegabytes = 1;
        using (var saved = await _admin.PutAsJsonAsync("/api/settings", settings.Values, Json, Token))
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Token));
        using (var scan = await _admin.PostAsJsonAsync("/api/scan", new ScanRequest(""), Json, Token))
            Assert.IsTrue(scan.IsSuccessStatusCode);
        for (var attempt = 0; (await _admin.GetFromJsonAsync<ScanStatus>("/api/scan", Json, Token))!.State == ScanState.Running; attempt++)
        {
            Assert.IsLessThan(200, attempt, "The scan did not finish in time");
            await Task.Delay(50, Token);
        }

        using var tooLarge = await _admin.PostAsJsonAsync("/api/files/archive", new ArchiveRequest(["Media"]), Json, Token);
        using var small = await _admin.PostAsJsonAsync("/api/files/archive", new ArchiveRequest(["ChildDir"]), Json, Token);

        Assert.AreEqual(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        StringAssert.Contains(await tooLarge.Content.ReadAsStringAsync(Token), "at most 1 MB");
        Assert.AreEqual(HttpStatusCode.OK, small.StatusCode);
    }

    private async Task<ArchiveLink> LinkAsync(HttpClient client, params string[] paths)
    {
        using var response = await client.PostAsJsonAsync("/api/files/archive", new ArchiveRequest(paths), Json, Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        var link = (await response.Content.ReadFromJsonAsync<ArchiveLink>(Json, Token))!;
        return link with { Url = "/" + link.Url };
    }

    private async Task<ZipArchive> ZipAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        var buffer = new MemoryStream(await response.Content.ReadAsByteArrayAsync(Token));
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private bool Link(string relative, string target)
    {
        var link = _library.FullPath(relative);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                })!;
                process.WaitForExit();
                if (process.ExitCode != 0)
                    return false;
            }
            else
            {
                Directory.CreateSymbolicLink(link, target);
            }
            _links.Add(link);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
