using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Text files through HTTP (<c>api/files/text</c>). Alice is in Users, with SpecialDir hidden from them and Media read
/// only; Bob may browse but not open files.
/// </summary>
[TestClass]
public sealed class TextRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
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
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!;
        var browsers = (await accounts.CreateGroupAsync(tenant, new GroupInput("Browsers", Rights: [Rights.Browse]), cancellationToken: Token)).Value!;
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("bob", Password: "bob-password-1", GroupIds: [browsers.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(users.Id), FolderLevel.Hidden, null, Token)).Status);
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "Media", FolderPrincipalRef.Group(users.Id), FolderLevel.Read, null, Token)).Status);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _bob?.Dispose();
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ATextFileComesWithItsEncodingLineEndingAndVersion()
    {
        await File.WriteAllBytesAsync(_library.FullPath("Media/notes.txt"), [0x80, (byte)'\r', (byte)'\n', (byte)'b'], Token);

        using var response = await _alice.GetAsync("/api/files/text?path=Media/notes.txt", Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        var text = (await response.Content.ReadFromJsonAsync<TextFile>(Json, Token))!;

        Assert.AreEqual(("€\r\nb", TextCodec.Windows1252, "crlf", 4L), (text.Text, text.Encoding, text.LineEnding, text.Size), "Media is read only for Alice: reading is fine");
        Assert.AreEqual($"\"{text.Version}\"", response.Headers.ETag?.ToString());
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
    }

    [TestMethod]
    public async Task TheRightAndTheFolderRulesApply()
    {
        _library.AddFile(".sfe/trash/x.txt", 10);

        using (var forbidden = await _bob.GetAsync("/api/files/text?path=RFile1.txt", Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode, "Bob may not open files");
        foreach (var path in new[] { "SpecialDir/SDРусский.txt", "Nothing.txt", "../Root/RFile1.txt", ".sfe/trash/x.txt", "", "ChildDir" })
        {
            using var response = await _alice.GetAsync($"/api/files/text?path={Uri.EscapeDataString(path)}", Token);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, path);
        }
        using (var hiddenForAlice = await _admin.GetAsync("/api/files/text?path=SpecialDir/SDРусский.txt", Token))
            Assert.AreEqual(HttpStatusCode.OK, hiddenForAlice.StatusCode);
    }

    [TestMethod]
    public async Task BinaryAndLargeFilesAreRefusedWithoutServerPaths()
    {
        await File.WriteAllBytesAsync(_library.FullPath("RFile2"), [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00], Token);
        _library.AddFile("big.log", TextEndpoints.MaxBytes + 1);

        using var binary = await _alice.GetAsync("/api/files/text?path=RFile2", Token);
        using var big = await _alice.GetAsync("/api/files/text?path=big.log", Token);
        var binaryBody = await binary.Content.ReadAsStringAsync(Token);
        var bigBody = await big.Content.ReadAsStringAsync(Token);

        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, binary.StatusCode);
        StringAssert.Contains(binaryBody, TextEndpoints.NotTextType);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        StringAssert.Contains(bigBody, TextEndpoints.TooLargeType);
        foreach (var body in new[] { binaryBody, bigBody })
            Assert.DoesNotContain(_library.Root, body.Replace("\\\\", "\\"), "no server path in an error");
    }

    [TestMethod]
    public async Task UnicodeTextRoundTrips()
    {
        const string text = "Ünïcödé — 写真\n";
        await File.WriteAllTextAsync(_library.FullPath("ChildDir/unicode.md"), text, new UTF8Encoding(false), Token);

        var read = (await _alice.GetFromJsonAsync<TextFile>("/api/files/text?path=ChildDir/unicode.md", Json, Token))!;

        Assert.AreEqual((text, TextCodec.Utf8, "lf"), (read.Text, read.Encoding, read.LineEnding));
    }
}
