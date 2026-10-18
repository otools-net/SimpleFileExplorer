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
/// only; Bob may browse but not open files. Eve may also delete and create files (Media read only for her too); Cara may
/// create files, not delete them.
/// </summary>
[TestClass]
public sealed class TextRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private HttpClient _eve = null!;
    private HttpClient _cara = null!;
    private int _tenant;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public Task InitializeAsync()
    {
        _library = new TestLibrary();
        return StartAsync();
    }

    private async Task StartAsync(Action<IDictionary<string, string?>>? configure = null, params string[] editorRights)
    {
        _factory = ApiClients.CreateFactory(_library, configure);
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(Token)).Single(t => t.IsDefault).Id;
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, Token))!;
        _tenant = tenant;
        var browsers = (await accounts.CreateGroupAsync(tenant, new GroupInput("Browsers", Rights: [Rights.Browse]), cancellationToken: Token)).Value!;
        var editors = (await accounts.CreateGroupAsync(tenant, new GroupInput("Editors",
            Rights: [Rights.Browse, Rights.ViewFiles, Rights.Download, Rights.CreateFiles, .. editorRights.Length > 0 ? editorRights : [Rights.DeleteFiles]]), cancellationToken: Token)).Value!;
        var copiers = (await accounts.CreateGroupAsync(tenant, new GroupInput("Copiers", Rights: [Rights.Browse, Rights.ViewFiles, Rights.CreateFiles]), cancellationToken: Token)).Value!;
        await accounts.CreateUserAsync(new UserInput("eve", Password: "eve-password-1", GroupIds: [editors.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("cara", Password: "cara-password-1", GroupIds: [copiers.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [users.Id]), null, Token);
        await accounts.CreateUserAsync(new UserInput("bob", Password: "bob-password-1", GroupIds: [browsers.Id]), null, Token);
        var rules = _factory.Services.GetRequiredService<FolderRuleService>();
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "SpecialDir", FolderPrincipalRef.Group(users.Id), FolderLevel.Hidden, null, Token)).Status);
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "Media", FolderPrincipalRef.Group(users.Id), FolderLevel.Read, null, Token)).Status);
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "Media", FolderPrincipalRef.Group(editors.Id), FolderLevel.Read, null, Token)).Status);
        _eve = await ApiClients.SignInAsync(_factory, "eve", "eve-password-1", Token);
        _cara = await ApiClients.SignInAsync(_factory, "cara", "cara-password-1", Token);
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password-1", Token);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Token);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Stop();
        _library.Dispose();
    }

    private void Stop()
    {
        _cara?.Dispose();
        _eve?.Dispose();
        _bob?.Dispose();
        _alice?.Dispose();
        _admin?.Dispose();
        _factory?.Dispose();
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

    /* ---------- Saving ---------- */

    [TestMethod]
    public async Task TheAnswerSaysWhatSavingMayDoAsTheRightsAndRulesSay()
    {
        WriteText("Docs/notes.txt", "notes");
        WriteText("Media/notes.txt", "notes");

        Assert.AreEqual(new TextAccess(false, false, true, false), (await ReadAsync(_alice, "Docs/notes.txt")).Access);
        Assert.AreEqual(new TextAccess(true, true, true, false), (await ReadAsync(_eve, "Docs/notes.txt")).Access);
        Assert.AreEqual(new TextAccess(false, false, true, false), (await ReadAsync(_eve, "Media/notes.txt")).Access, "Media is read only for Eve");
        Assert.AreEqual(new TextAccess(false, true, false, false), (await ReadAsync(_cara, "Docs/notes.txt")).Access, "Cara may only save copies");
    }

    [TestMethod]
    public async Task SavingOverNeedsTheVersionItStartedFrom()
    {
        var file = WriteText("Docs/notes.txt", "one\r\ntwo\r\n");
        var opened = await ReadAsync(_eve, "Docs/notes.txt");

        using (var missing = await SaveAsync(_eve, "Docs/notes.txt", "x", null))
            Assert.AreEqual(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        SavedText saved;
        using (var ok = await SaveAsync(_eve, "Docs/notes.txt", "one\nTWO\n", opened.Version))
        {
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, await ok.Content.ReadAsStringAsync(Token));
            saved = (await ok.Content.ReadFromJsonAsync<SavedText>(Json, Token))!;
            Assert.AreEqual($"\"{saved.Version}\"", ok.Headers.ETag?.ToString());
        }
        Assert.AreEqual("one\r\nTWO\r\n", File.ReadAllText(file), "The file's line endings and encoding are kept");
        Assert.IsNotNull(saved.TrashId, "The previous version is in the trash");
        using (var stale = await SaveAsync(_eve, "Docs/notes.txt", "three", opened.Version))
        {
            Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
            StringAssert.Contains(await stale.Content.ReadAsStringAsync(Token), TextEndpoints.ChangedType);
        }
        using (var anyway = await SaveAsync(_eve, "Docs/notes.txt", "three", "*"))
            Assert.AreEqual(HttpStatusCode.OK, anyway.StatusCode);
        Assert.AreEqual("three", File.ReadAllText(file));
    }

    [TestMethod]
    public async Task OnlyThoseWhoMayReplaceTheFileSaveOverIt()
    {
        var file = WriteText("Docs/notes.txt", "notes");
        WriteText("Media/notes.txt", "notes");
        WriteText("SpecialDir/notes.txt", "notes");

        using (var alice = await SaveAsync(_alice, "Docs/notes.txt", "x", "*"))
            Assert.AreEqual(HttpStatusCode.Forbidden, alice.StatusCode, "No right to delete at all");
        using (var cara = await SaveAsync(_cara, "Docs/notes.txt", "x", "*"))
            Assert.AreEqual(HttpStatusCode.Forbidden, cara.StatusCode);
        using (var readOnly = await SaveAsync(_eve, "Media/notes.txt", "x", "*"))
            Assert.AreEqual(HttpStatusCode.Forbidden, readOnly.StatusCode, "A read-only folder");
        using (var hidden = await SaveAsync(_alice, "SpecialDir/notes.txt", "x", "*"))
            Assert.AreEqual(HttpStatusCode.Forbidden, hidden.StatusCode, "The route's right comes first");
        using (var badSession = await SaveAsync(_eve, "Docs/notes.txt", "x", "*", session: "../x"))
            Assert.AreEqual(HttpStatusCode.BadRequest, badSession.StatusCode);
        Assert.AreEqual("notes", File.ReadAllText(file));
    }

    [TestMethod]
    public async Task TextTheEncodingCantHoldIsRefusedWithTheCharacters()
    {
        var file = WriteText("Docs/old.txt", "caf\u00e9");
        await File.WriteAllBytesAsync(file, [0x63, 0x61, 0x66, 0xE9], Token);
        var opened = await ReadAsync(_eve, "Docs/old.txt");
        Assert.AreEqual(TextCodec.Windows1252, opened.Encoding);

        using var refused = await SaveAsync(_eve, "Docs/old.txt", "café 東京", opened.Version, encoding: opened.Encoding);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        var body = await refused.Content.ReadAsStringAsync(Token);
        StringAssert.Contains(body, TextEndpoints.NotEncodableType);
        StringAssert.Contains(body, "東");
        CollectionAssert.AreEqual(new byte[] { 0x63, 0x61, 0x66, 0xE9 }, await File.ReadAllBytesAsync(file, Token));
    }

    [TestMethod]
    public async Task ACopyIsSavedBesideNeverOverAnything()
    {
        WriteText("Docs/notes.txt", "notes");
        WriteText("SpecialDir/hidden.txt", "hidden");

        using (var copy = await _cara.PostAsJsonAsync("/api/files/text", new CopyTextRequest("Docs", "notes (copy).txt", "edited", TextCodec.Utf8, "lf"), Json, Token))
        {
            Assert.AreEqual(HttpStatusCode.OK, copy.StatusCode, await copy.Content.ReadAsStringAsync(Token));
            Assert.AreEqual("Docs/notes (copy).txt", (await copy.Content.ReadFromJsonAsync<Sfe.Core.Library.LibraryItem>(Json, Token))!.Path);
        }
        Assert.AreEqual("edited", File.ReadAllText(_library.FullPath("Docs/notes (copy).txt")));
        using (var clash = await _cara.PostAsJsonAsync("/api/files/text", new CopyTextRequest("Docs", "notes.txt", "over", TextCodec.Utf8, "lf"), Json, Token))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, clash.StatusCode);
            StringAssert.Contains(await clash.Content.ReadAsStringAsync(Token), FileEndpoints.ExistsType);
        }
        Assert.AreEqual("notes", File.ReadAllText(_library.FullPath("Docs/notes.txt")));
        using (var alice = await _alice.PostAsJsonAsync("/api/files/text", new CopyTextRequest("Docs", "x.txt", "x", TextCodec.Utf8, "lf"), Json, Token))
            Assert.AreEqual(HttpStatusCode.Forbidden, alice.StatusCode);
        using (var work = await _cara.PostAsJsonAsync("/api/files/text", new CopyTextRequest(".sfe", "x.txt", "x", TextCodec.Utf8, "lf"), Json, Token))
            Assert.AreEqual(HttpStatusCode.NotFound, work.StatusCode);
    }

    [TestMethod]
    public async Task ASpaceThatDeletesForGoodNeedsThatRightAndKeepsNoPreviousVersion()
    {
        await RestartAsync(s => s["Sfe:Files:DeletePermanently"] = "true");
        WriteText("Docs/notes.txt", "notes");
        Assert.IsFalse((await ReadAsync(_eve, "Docs/notes.txt")).Access!.Overwrite, "files.delete is not enough there");

        await RestartAsync(s => s["Sfe:Files:DeletePermanently"] = "true", Rights.DeletePermanently);
        WriteText("Docs/notes.txt", "notes");
        var opened = await ReadAsync(_eve, "Docs/notes.txt");
        Assert.AreEqual(new TextAccess(true, true, true, true), opened.Access);
        using var saved = await SaveAsync(_eve, "Docs/notes.txt", "new", opened.Version);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(Token));
        Assert.IsNull((await saved.Content.ReadFromJsonAsync<SavedText>(Json, Token))!.TrashId);
    }

    /// <summary>Another server, on a library of its own (the first one's database is set up already).</summary>

    [TestMethod]
    public async Task AFileIsReadAgainInTheEncodingAsked()
    {
        await File.WriteAllBytesAsync(_library.FullPath("Media/notes.txt"), [0x63, 0x61, 0x66, 0xC3, 0xA9], Token);

        var asIs = await ReadAsync(_alice, "Media/notes.txt");
        using var ansi = await _alice.GetAsync("/api/files/text?path=Media/notes.txt&encoding=windows-1252", Token);
        var asAnsi = (await ansi.Content.ReadFromJsonAsync<TextFile>(Json, Token))!;
        using var unknown = await _alice.GetAsync("/api/files/text?path=Media/notes.txt&encoding=latin-9", Token);

        Assert.AreEqual(("café", TextCodec.Utf8), (asIs.Text, asIs.Encoding));
        Assert.AreEqual(("cafÃ©", TextCodec.Windows1252, asIs.Version), (asAnsi.Text, asAnsi.Encoding, asAnsi.Version));
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [TestMethod]
    public async Task LargeFilesAreReadInPartsWithTheSameRules()
    {
        _library.AddFile(".sfe/trash/x.log", 10);
        WriteText("big.log", string.Concat(Enumerable.Range(1, 1000).Select(i => $"line {i:0000}\n")));
        await File.WriteAllBytesAsync(_library.FullPath("RFile2"), [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00], Token);

        var start = await PartAsync(_alice, "big.log&offset=0&length=100");
        var end = await PartAsync(_alice, "big.log&length=100");
        using var binary = await _alice.GetAsync("/api/files/text/part?path=RFile2", Token);
        using var tooLong = await _alice.GetAsync($"/api/files/text/part?path=big.log&length={TextEndpoints.MaxPartBytes + 1}", Token);
        using var forbidden = await _bob.GetAsync("/api/files/text/part?path=big.log", Token);
        using var work = await _alice.GetAsync("/api/files/text/part?path=.sfe/trash/x.log", Token);
        using var hidden = await _alice.GetAsync($"/api/files/text/part?path={Uri.EscapeDataString("SpecialDir/SDРусский.txt")}", Token);

        Assert.AreEqual((0L, 100L, 10_000L), (start.Offset, start.End, start.Size));
        StringAssert.StartsWith(start.Text, "line 0001\n");
        Assert.AreEqual((9_900L, 10_000L), (end.Offset, end.End));
        StringAssert.EndsWith(end.Text, "line 1000\n");
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, binary.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, work.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [TestMethod]
    public async Task TheVersionTellsWhetherTheFileChangedSinceItWasOpened()
    {
        WriteText("Media/notes.txt", "first");
        _library.AddFile("big.log", TextEndpoints.MaxBytes + 1);
        var opened = await ReadAsync(_alice, "Media/notes.txt");

        var same = await VersionAsync(_alice, $"Media/notes.txt&known={Uri.EscapeDataString(opened.Version)}");
        WriteText("Media/notes.txt", "second!");
        var changed = await VersionAsync(_alice, $"Media/notes.txt&known={Uri.EscapeDataString(opened.Version)}");
        var big = await VersionAsync(_alice, "big.log");
        using var forbidden = await _bob.GetAsync("/api/files/text/version?path=Media/notes.txt", Token);

        Assert.AreEqual(opened.Version, same.Version);
        Assert.AreNotEqual(opened.Version, changed.Version);
        Assert.AreEqual(7L, changed.Size);
        Assert.AreEqual((null, TextEndpoints.MaxBytes + 1), (big.Version, big.Size));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private async Task<TextPart> PartAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"/api/files/text/part?path={query}", Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TextPart>(Json, Token))!;
    }

    private async Task<TextVersion> VersionAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"/api/files/text/version?path={query}", Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TextVersion>(Json, Token))!;
    }
    private Task RestartAsync(Action<IDictionary<string, string?>> configure, params string[] editorRights)
    {
        Stop();
        _library.Dispose();
        _library = new TestLibrary();
        return StartAsync(configure, editorRights);
    }

    private async Task<TextFile> ReadAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"/api/files/text?path={Uri.EscapeDataString(path)}", Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TextFile>(Json, Token))!;
    }

    private Task<HttpResponseMessage> SaveAsync(HttpClient client, string path, string text, string? version, string session = "session-1", string encoding = TextCodec.Utf8)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/files/text?path={Uri.EscapeDataString(path)}")
        {
            Content = JsonContent.Create(new SaveTextRequest(text, encoding, "crlf", session), options: Json),
        };
        if (version is not null)
            request.Headers.TryAddWithoutValidation("If-Match", version == "*" ? "*" : $"\"{version}\"");
        return client.SendAsync(request, Token);
    }

    private string WriteText(string relative, string text)
    {
        var full = _library.FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }
}
