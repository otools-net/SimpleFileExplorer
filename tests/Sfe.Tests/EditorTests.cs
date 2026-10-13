using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Editing;
using Sfe.Core.Library;
using Sfe.Core.Search;
using Sfe.Core.Settings;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>The picture editor's saves (<see cref="EditedImages"/>), its route, right and settings.</summary>
[TestClass]
public sealed class EditorTests
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
    public async Task SavesANewFileUnderTheOriginalsFoldersInTheOutputFolder()
    {
        await using var core = await CoreServices.CreateAsync(_library);
        var editor = core.Get<EditedImages>();
        var original = await File.ReadAllBytesAsync(_library.FullPath("Media/photo.jpg"), TestContext.CancellationToken);

        var first = await editor.SaveAsync("Media/photo.jpg", new MemoryStream(Png(640, 480)), TestContext.CancellationToken);
        var second = await editor.SaveAsync("Media/photo.jpg", new MemoryStream(Png(320, 200)), TestContext.CancellationToken);
        var search = await core.Get<FileSearchService>().SearchAsync("photo", null, null, 10, SortField.Path, false, TestContext.CancellationToken);

        Assert.AreEqual(EditedImageOutcome.Saved, first.Outcome, first.Message);
        Assert.AreEqual(new EditedImage("Edited/Media/photo.png", 640, 480, Png(640, 480).Length), first.Image);
        Assert.AreEqual("Edited/Media/photo (2).png", second.Image!.Path, "An existing file is never written over");
        CollectionAssert.AreEqual(Png(640, 480), await File.ReadAllBytesAsync(_library.FullPath("Edited/Media/photo.png"), TestContext.CancellationToken));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(_library.FullPath("Media/photo.jpg"), TestContext.CancellationToken), "The original is untouched");
        CollectionAssert.IsSubsetOf(new[] { "Edited/Media/photo (2).png", "Edited/Media/photo.png" }, search!.Items.Select(i => i.Path).ToList(),
            "Indexed at once, before the next scan");
    }

    [TestMethod]
    public async Task APictureOfTheOriginalsFormatKeepsItsExtension()
    {
        _library.AddFile("Photos/Beach.JPEG", 100);
        await using var core = await CoreServices.CreateAsync(_library);

        var result = await core.Get<EditedImages>().SaveAsync("Photos/Beach.JPEG", new MemoryStream(Jpeg(800, 600)), TestContext.CancellationToken);

        Assert.AreEqual("Edited/Photos/Beach.JPEG", result.Image?.Path, result.Message);
        Assert.AreEqual((800, 600), (result.Image!.Width, result.Image.Height));
    }

    [TestMethod]
    public async Task WithoutAnOutputFolderTheEditGoesNextToTheOriginal()
    {
        await using var core = await CoreServices.CreateAsync(_library, settings => settings["Sfe:Editor:OutputFolder"] = "");
        var editor = core.Get<EditedImages>();

        var result = await editor.SaveAsync("Media/photo.jpg", new MemoryStream(Jpeg(10, 10)), TestContext.CancellationToken);
        var again = await editor.SaveAsync(result.Image!.Path, new MemoryStream(Jpeg(5, 5)), TestContext.CancellationToken);

        Assert.AreEqual("Media/photo (edited).jpg", result.Image.Path);
        Assert.AreEqual("Media/photo (edited) (edited).jpg", again.Image?.Path);
    }

    [TestMethod]
    public async Task EditingAnEditedPictureSavesItBesideIt()
    {
        await using var core = await CoreServices.CreateAsync(_library, settings => settings["Sfe:Editor:OutputFolder"] = "Work/Edits");
        var editor = core.Get<EditedImages>();

        var first = await editor.SaveAsync("Media/photo.jpg", new MemoryStream(Jpeg(10, 10)), TestContext.CancellationToken);
        var second = await editor.SaveAsync(first.Image!.Path, new MemoryStream(Jpeg(5, 5)), TestContext.CancellationToken);

        Assert.AreEqual("Work/Edits/Media/photo.jpg", first.Image.Path);
        Assert.AreEqual("Work/Edits/Media/photo (edited).jpg", second.Image?.Path, "Not Work/Edits/Work/Edits/...");
    }

    [TestMethod]
    public async Task RefusesWhatIsNotAPictureOfTheLibrary()
    {
        await using var core = await CoreServices.CreateAsync(_library);
        var editor = core.Get<EditedImages>();

        var outside = await editor.SaveAsync("../outside.jpg", new MemoryStream(Png(1, 1)), TestContext.CancellationToken);
        var missing = await editor.SaveAsync("Media/none.jpg", new MemoryStream(Png(1, 1)), TestContext.CancellationToken);
        var notAnImage = await editor.SaveAsync("RFile1.txt", new MemoryStream(Png(1, 1)), TestContext.CancellationToken);
        var garbage = await editor.SaveAsync("Media/photo.jpg", new MemoryStream("<script>alert(1)</script>"u8.ToArray()), TestContext.CancellationToken);
        var gif = await editor.SaveAsync("Media/photo.jpg", new MemoryStream("GIF89a\x01\0\x01\0"u8.ToArray()), TestContext.CancellationToken);

        Assert.AreEqual(EditedImageOutcome.SourceNotFound, outside.Outcome);
        Assert.AreEqual(EditedImageOutcome.SourceNotFound, missing.Outcome);
        Assert.AreEqual(EditedImageOutcome.SourceNotFound, notAnImage.Outcome);
        Assert.AreEqual(EditedImageOutcome.InvalidImage, garbage.Outcome);
        Assert.AreEqual(EditedImageOutcome.InvalidImage, gif.Outcome, "Only what a browser's canvas writes");
        Assert.IsFalse(Directory.Exists(_library.FullPath("Edited")), "Nothing written");
    }

    [TestMethod]
    public async Task AnOutputFolderOutsideTheLibraryIsRefused()
    {
        await using var core = await CoreServices.CreateAsync(_library, settings => settings["Sfe:Editor:OutputFolder"] = "../Elsewhere");

        var result = await core.Get<EditedImages>().SaveAsync("Media/photo.jpg", new MemoryStream(Png(1, 1)), TestContext.CancellationToken);
        var (_, errors) = await core.Get<SettingsService>().SaveAsync(
            EditableSettings.From(new Sfe.Core.Options.SfeOptions { Root = _library.Root, Editor = { OutputFolder = "../x" } }), TestContext.CancellationToken);

        Assert.AreEqual(EditedImageOutcome.CannotWrite, result.Outcome);
        Assert.IsFalse(Directory.Exists(Path.Combine(_library.BasePath, "Elsewhere")));
        Assert.IsNotNull(errors);
        Assert.Contains("editor.outputFolder", errors.Keys);
    }

    [TestMethod]
    public async Task TheRouteSavesForThoseWithTheRight()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);

        var config = await GetAsync<ClientConfig>(admin, "/api/config");
        using var saved = await PostImageAsync(admin, "Media/photo.jpg", Jpeg(30, 20));
        var image = await saved.Content.ReadFromJsonAsync<EditedImage>(Json, TestContext.CancellationToken);
        using var invalid = await PostImageAsync(admin, "Media/photo.jpg", "not a picture"u8.ToArray());
        using var missing = await PostImageAsync(admin, "Media/missing.jpg", Jpeg(30, 20));

        Assert.AreEqual(new EditorConfig("Edited", 92), config.Editor);
        Assert.AreEqual(HttpStatusCode.Created, saved.StatusCode);
        Assert.AreEqual(new EditedImage("Edited/Media/photo.jpg", 30, 20, Jpeg(30, 20).Length), image);
        Assert.IsTrue(File.Exists(_library.FullPath("Edited/Media/photo.jpg")));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [TestMethod]
    public async Task GuestsAndAccountsWithoutTheRightCannotSave()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        using (await admin.PutAsJsonAsync("/api/access/tenant/guest-access", new GuestAccessChange(true), TestContext.CancellationToken)) { }
        var groups = await GetAsync<List<GroupInfo>>(admin, "/api/access/groups");
        var users = groups.Single(g => g.BuiltIn == BuiltInGroup.Users);
        var guests = groups.Single(g => g.BuiltIn == BuiltInGroup.Guests);
        using (var create = await admin.PostAsJsonAsync("/api/access/users", new UserInput("alice", Password: "alice-password", GroupIds: [users.Id]), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", TestContext.CancellationToken);
        _disposables.Add(alice);
        var visitor = ApiClients.Create(factory);
        _disposables.Add(visitor);

        using var guestGrant = await admin.PutAsJsonAsync($"/api/access/groups/{guests.Id}",
            new GroupInput(guests.Name, Rights: [.. guests.Rights, Rights.EditImages]), Json, TestContext.CancellationToken);
        using var byVisitor = await PostImageAsync(visitor, "Media/photo.jpg", Png(2, 2));
        var visitorConfig = await GetAsync<ClientConfig>(visitor, "/api/config");
        using var byUser = await PostImageAsync(alice, "Media/photo.jpg", Png(2, 2));
        using (var revoke = await admin.PutAsJsonAsync($"/api/access/groups/{users.Id}",
            new GroupInput(users.Name, Rights: users.Rights.Where(r => r != Rights.EditImages).ToList()), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode);
        using var afterRevoke = await PostImageAsync(alice, "Media/photo.jpg", Png(2, 2));
        var aliceConfig = await GetAsync<ClientConfig>(alice, "/api/config");

        Assert.AreEqual(HttpStatusCode.BadRequest, guestGrant.StatusCode, "A guest may never edit");
        Assert.AreEqual(HttpStatusCode.Unauthorized, byVisitor.StatusCode, "Visitors without an account are asked to sign in");
        Assert.IsNull(visitorConfig.Editor);
        Assert.AreEqual(HttpStatusCode.Created, byUser.StatusCode, "Users may edit by default");
        Assert.AreEqual(HttpStatusCode.Forbidden, afterRevoke.StatusCode);
        Assert.IsNull(aliceConfig.Editor);
        Assert.IsFalse(File.Exists(_library.FullPath("Edited/Media/photo (2).png")));
    }

    [TestMethod]
    public async Task TheOutputFolderIsASettingOfEachLibrary()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var view = await GetAsync<SettingsView>(admin, "/api/settings?level=tenant");
        view.Values.Editor.OutputFolder = @"Photos\Edited/";
        view.Values.Editor.Quality = 80;

        using var save = await admin.PutAsJsonAsync("/api/settings?level=tenant", view.Values, Json, TestContext.CancellationToken);
        var config = await GetAsync<ClientConfig>(admin, "/api/config");
        var server = await GetAsync<SettingsView>(admin, "/api/settings?level=server");
        using var saved = await PostImageAsync(admin, "Media/photo.jpg", Png(4, 4));
        view.Values.Editor.OutputFolder = "../outside";
        using var invalid = await admin.PutAsJsonAsync("/api/settings?level=tenant", view.Values, Json, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, save.StatusCode, await save.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(new EditorConfig(OperatingSystem.IsWindows() ? "Photos/Edited" : @"Photos\Edited", 80), config.Editor);
        Assert.IsFalse(server.Keys!["editor.outputFolder"].Editable, "Paths of a library have no server-wide value");
        Assert.AreEqual(Rights.EditorSettings, SettingCatalog.Find("editor.quality")!.Right);
        Assert.AreEqual(HttpStatusCode.Created, saved.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        StringAssert.Contains(await invalid.Content.ReadAsStringAsync(TestContext.CancellationToken), "editor.outputFolder");
    }

    [TestMethod]
    public async Task AnUpgradeGrantsTheNewRightsOnce()
    {
        // A space created by 1.34: no revision, its groups without the rights added since.
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var added = RightCatalog.All.Where(r => r.Since > 0).Select(r => r.Id).ToList();
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(TestContext.CancellationToken))
        {
            await db.GroupRights.Where(r => added.Contains(r.Right)).ExecuteDeleteAsync(TestContext.CancellationToken);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Tenants DROP COLUMN RightsRevision", TestContext.CancellationToken);
        }

        var upgraded = await RestartAsync();
        var users = await GroupAsync(upgraded, BuiltInGroup.Users);
        var administrators = await GroupAsync(upgraded, BuiltInGroup.Administrators);
        var guests = await GroupAsync(upgraded, BuiltInGroup.Guests);
        using (var revoke = await upgraded.PutAsJsonAsync($"/api/access/groups/{users.Id}",
            new GroupInput(users.Name, Rights: users.Rights.Where(r => r != Rights.EditImages).ToList()), Json, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode);
        var restarted = await RestartAsync();

        CollectionAssert.Contains(users.Rights.ToList(), Rights.EditImages);
        CollectionAssert.DoesNotContain(users.Rights.ToList(), Rights.EditorSettings);
        CollectionAssert.IsSubsetOf(added, administrators.Rights.ToList());
        CollectionAssert.DoesNotContain(guests.Rights.ToList(), Rights.EditImages);
        CollectionAssert.DoesNotContain((await GroupAsync(restarted, BuiltInGroup.Users)).Rights.ToList(), Rights.EditImages,
            "A right taken away after the upgrade stays away");
    }

    private async Task<HttpClient> RestartAsync() =>
        await ApiClients.SignInAsync(Factory(), ApiClients.AdminName, ApiClients.AdminPassword, TestContext.CancellationToken);

    private WebApplicationFactory<Program> Factory()
    {
        var factory = ApiClients.CreateFactory(_library);
        _disposables.Add(factory);
        return factory;
    }

    private Task<HttpResponseMessage> PostImageAsync(HttpClient client, string path, byte[] content)
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return client.PostAsync($"/api/editor/images?path={Uri.EscapeDataString(path)}", body, TestContext.CancellationToken);
    }

    private async Task<GroupInfo> GroupAsync(HttpClient admin, BuiltInGroup builtIn) =>
        (await GetAsync<List<GroupInfo>>(admin, "/api/access/groups")).Single(g => g.BuiltIn == builtIn);

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {await response.Content.ReadAsStringAsync(TestContext.CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }

    /// <summary>The start of a PNG: its signature and header chunk, which is what the server reads.</summary>
    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }

    /// <summary>The start of a JPEG: its start marker and frame header.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0, 17, 8, 0, 0, 0, 0, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1, 0xFF, 0xD9 };
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(7), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(9), (ushort)width);
        return bytes;
    }
}
