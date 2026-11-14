using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.FileTracking;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Core.Tasks;
using Otools.Net.Sfe.Core.Trickplay;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

[TestClass]
public sealed class ApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in _library.Settings())
                builder.UseSetting(key, value);
        });
        // Signed in as the server administrator the setup creates: every right.
        _client = await ApiClients.SetUpAsync(_factory, Token);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ConfigDescribesTheLibrary()
    {
        var config = await GetAsync<ClientConfig>($"{V1}/config");

        Assert.AreEqual("Root", config.RootName);
        Assert.IsNull(config.DataServer);
        Assert.IsFalse(config.Features.Trickplay);
        Assert.AreEqual(TagService.MaxFilesPerChange, config.MaxFilesPerTagChange);
        Assert.AreEqual(10, config.SeekSeconds);
        Assert.AreEqual(new ExternalPlayer("VLC", "vlc://{url}"), config.ExternalPlayer);
        Assert.IsTrue(config.TrickplayHover);
        Assert.IsTrue(config.SearchExplain);
        // Directory.Build.props, without the commit the SDK appends to the informational version.
        Assert.AreEqual(typeof(ClientConfig).Assembly.GetName().Version!.ToString(3), config.Version);
    }

    [TestMethod]
    public async Task SearchExplanationCanBeTurnedOff()
    {
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        Assert.IsTrue(settings.Values.Search.Explain);

        settings.Values.Search.Explain = false;
        using var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        Assert.IsFalse((await GetAsync<ClientConfig>($"{V1}/config")).SearchExplain);
        Assert.IsFalse((await GetAsync<SettingsView>($"{V1}/settings")).Values.Search.Explain);
    }

    [TestMethod]
    public async Task VideoLoopCanBeTurnedOnByDefault()
    {
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        Assert.IsFalse(settings.Values.Player.Loop);
        Assert.IsFalse((await GetAsync<ClientConfig>($"{V1}/config")).Loop);

        settings.Values.Player.Loop = true;
        using var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        Assert.IsTrue((await GetAsync<ClientConfig>($"{V1}/config")).Loop);
        Assert.IsTrue((await GetAsync<SettingsView>($"{V1}/settings")).Values.Player.Loop);
    }

    [TestMethod]
    public async Task SearchHistorySizeIsSentToTheClientAndChecked()
    {
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        Assert.AreEqual(10, settings.Values.Search.HistorySize);
        Assert.AreEqual(10, (await GetAsync<ClientConfig>($"{V1}/config")).SearchHistory);

        settings.Values.Search.HistorySize = 0;
        using var off = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);
        var config = await GetAsync<ClientConfig>($"{V1}/config");
        settings.Values.Search.HistorySize = SettingsService.MaxSearchHistory + 1;
        using var tooMany = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, off.StatusCode);
        Assert.AreEqual(0, config.SearchHistory);
        Assert.AreEqual(HttpStatusCode.BadRequest, tooMany.StatusCode);
        StringAssert.Contains(await tooMany.Content.ReadAsStringAsync(Token), "search.historySize");
    }

    [TestMethod]
    public async Task SingleViewOriginalsAreOffByDefaultAndCanBeTurnedOn()
    {
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        Assert.IsFalse(settings.Values.Thumbnails.SingleViewOriginals);
        Assert.IsFalse((await GetAsync<ClientConfig>($"{V1}/config")).SingleViewOriginals);

        settings.Values.Thumbnails.SingleViewOriginals = true;
        using var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        Assert.IsTrue((await GetAsync<ClientConfig>($"{V1}/config")).SingleViewOriginals);
        Assert.IsTrue((await GetAsync<SettingsView>($"{V1}/settings")).Values.Thumbnails.SingleViewOriginals);
    }

    [TestMethod]
    public async Task AnimatedHoverIsOnByDefaultAndCanBeTurnedOff()
    {
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        Assert.IsTrue(settings.Values.Thumbnails.AnimatedHover);
        Assert.IsTrue((await GetAsync<ClientConfig>($"{V1}/config")).AnimatedHover);

        settings.Values.Thumbnails.AnimatedHover = false;
        using var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        Assert.IsFalse((await GetAsync<ClientConfig>($"{V1}/config")).AnimatedHover);
    }

    [TestMethod]
    public async Task PicturesSayWhetherTheyAreAnimated()
    {
        await File.WriteAllBytesAsync(
            _library.FullPath("Media/moving #1.gif"),
            ImageAnimationTests.Gif(frames: 2),
            Token
        );
        await File.WriteAllBytesAsync(_library.FullPath("Media/still.gif"), ImageAnimationTests.Gif(frames: 1), Token);

        Assert.IsTrue((await GetAsync<ImageAnimationInfo>($"{V1}/files/animated/Media/moving%20%231.gif")).Animated);
        Assert.IsFalse((await GetAsync<ImageAnimationInfo>($"{V1}/files/animated/Media/still.gif")).Animated);
        Assert.IsFalse((await GetAsync<ImageAnimationInfo>($"{V1}/files/animated/Media/photo.jpg")).Animated);
    }

    [TestMethod]
    public async Task ExternalPlayerCanBeChangedOrRemoved()
    {
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        settings.Values.Player.ExternalName = "PotPlayer";
        settings.Values.Player.ExternalUrl = "potplayer://";
        using (var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token))
        {
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        }

        Assert.AreEqual(
            new ExternalPlayer("PotPlayer", "potplayer://"),
            (await GetAsync<ClientConfig>($"{V1}/config")).ExternalPlayer
        );

        settings.Values.Player.ExternalUrl = "";
        using (var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token))
        {
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        }

        Assert.IsNull((await GetAsync<ClientConfig>($"{V1}/config")).ExternalPlayer);
    }

    [TestMethod]
    public async Task RootPathIsOnlySentWhenCopyingPathsIsOn()
    {
        Assert.IsNull((await GetAsync<ClientConfig>($"{V1}/config")).RootPath);
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        Assert.IsFalse(settings.Values.Browse.CopyPaths);

        settings.Values.Browse.CopyPaths = true;
        using var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        // The client appends the root-relative paths to it, with the separator it ends with.
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_library.Root)) + Path.DirectorySeparatorChar,
            (await GetAsync<ClientConfig>($"{V1}/config")).RootPath
        );
        Assert.IsTrue((await GetAsync<SettingsView>($"{V1}/settings")).Values.Browse.CopyPaths);
    }

    [TestMethod]
    public async Task DefaultThemeCanBeChanged()
    {
        Assert.AreEqual(Theme.System, (await GetAsync<ClientConfig>($"{V1}/config")).Theme);
        var settings = await GetAsync<SettingsView>($"{V1}/settings");

        settings.Values.Appearance.Theme = Theme.Light;
        using var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        // The client compares the names: the API sends them in camelCase.
        using var config = await _client.GetAsync($"{V1}/config", Token);
        StringAssert.Contains(await config.Content.ReadAsStringAsync(Token), "\"theme\":\"light\"");
    }

    [TestMethod]
    public async Task BrowseListsFoldersFirstInNaturalOrder()
    {
        var root = await GetAsync<DirectoryListing>($"{V1}/browse");

        Assert.AreEqual("", root.Path);
        Assert.IsNull(root.ParentPath);
        Assert.AreEqual(4, root.DirectoryCount);
        CollectionAssert.AreEqual(
            new[] { "ChildDir", "Media", "RepeatedDir", "SpecialDir", "100%_done[1].txt", "RFile1.txt", "RFile2" },
            root.Items.Select(i => i.Name).ToArray()
        );

        var media = await GetAsync<DirectoryListing>($"{V1}/browse?path=Media");
        CollectionAssert.AreEqual(
            new[] { "Episode 2.mkv", "Episode 10.mkv", "Holiday 2020.mp4", "photo.jpg" },
            media.Items.Select(i => i.Name).ToArray()
        );
        Assert.AreEqual(ItemKind.Video, media.Items[0].Kind);
        Assert.AreEqual(ItemKind.Image, media.Items[3].Kind);
        Assert.AreEqual("", media.ParentPath);
        CollectionAssert.AreEqual(new[] { "Root", "Media" }, media.Breadcrumbs.Select(b => b.Name).ToArray());
    }

    [TestMethod]
    public async Task BrowseSortsAndPages()
    {
        var bySize = await GetAsync<DirectoryListing>($"{V1}/browse?path=Media&sort=size&desc=true&offset=1&limit=2");

        Assert.AreEqual(4, bySize.Total);
        CollectionAssert.AreEqual(
            new[] { "Episode 10.mkv", "Episode 2.mkv" },
            bySize.Items.Select(i => i.Name).ToArray()
        );
    }

    [TestMethod]
    public async Task SiblingsFollowTheParentsOrder()
    {
        var media = await GetAsync<FolderSiblings>($"{V1}/browse/siblings?path=Media");
        var first = await GetAsync<FolderSiblings>($"{V1}/browse/siblings?path=ChildDir");
        var reversed = await GetAsync<FolderSiblings>($"{V1}/browse/siblings?path=Media&sort=name&desc=true");
        var root = await GetAsync<FolderSiblings>($"{V1}/browse/siblings");

        Assert.AreEqual("ChildDir", media.Previous?.Path);
        Assert.AreEqual("RepeatedDir", media.Next?.Path);
        Assert.AreEqual(2, media.Position);
        Assert.AreEqual(4, media.Count);
        Assert.IsNull(first.Previous);
        Assert.AreEqual("Media", first.Next?.Path);
        Assert.AreEqual("RepeatedDir", reversed.Previous?.Path);
        Assert.AreEqual("ChildDir", reversed.Next?.Path);
        Assert.IsNull(root.Previous);
        Assert.IsNull(root.Next);
        Assert.AreEqual(0, root.Position);
    }

    [TestMethod]
    public async Task FolderCoversShowTheFirstPictures()
    {
        foreach (var name in new[] { "p10.jpg", "p2.png", "p1.jpg", "p3.webp", "p20.gif", "notes.txt", "song.mp3" })
            _library.AddFile($"Pictures/{name}", 10);
        _library.AddFile("Pictures/Inner/p0.jpg", 10);
        var thumbnails = (await GetAsync<ClientConfig>($"{V1}/config")).Features.Thumbnails;

        var covers = await PostCoversAsync("Pictures", "Media", "ChildDir", "..", "Missing");

        CollectionAssert.AreEqual(new[] { "Pictures", "Media", "ChildDir" }, covers.Select(c => c.Path).ToArray());
        CollectionAssert.AreEqual(
            new[] { "p1.jpg", "p2.png", "p3.webp", "p10.jpg" },
            covers[0].Items.Select(i => i.Name).ToArray()
        );
        // Videos only count when they have a thumbnail to show.
        CollectionAssert.AreEqual(
            thumbnails
                ? new[] { "Episode 2.mkv", "Episode 10.mkv", "Holiday 2020.mp4", "photo.jpg" }
                : new[] { "photo.jpg" },
            covers[1].Items.Select(i => i.Name).ToArray()
        );
        Assert.IsEmpty(covers[2].Items);

        // A file added to the folder changes its cover.
        _library.AddFile("Pictures/a.jpg", 10);
        var updated = await PostCoversAsync("Pictures");
        CollectionAssert.AreEqual(
            new[] { "a.jpg", "p1.jpg", "p2.png", "p3.webp" },
            updated[0].Items.Select(i => i.Name).ToArray()
        );

        // The number of pictures is a setting; 0 turns them off.
        Assert.AreEqual(4, (await GetAsync<ClientConfig>($"{V1}/config")).FolderPictures);
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        settings.Values.Browse.FolderPictures = 2;
        (await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token)).Dispose();
        var two = await PostCoversAsync("Pictures");
        settings.Values.Browse.FolderPictures = 0;
        (await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token)).Dispose();
        var off = await PostCoversAsync("Pictures");
        settings.Values.Browse.FolderPictures = LibraryBrowser.MaxFolderPictures + 1;
        using var invalid = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        CollectionAssert.AreEqual(new[] { "a.jpg", "p1.jpg" }, two[0].Items.Select(i => i.Name).ToArray());
        Assert.IsEmpty(off[0].Items);
        Assert.AreEqual(0, (await GetAsync<ClientConfig>($"{V1}/config")).FolderPictures);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        StringAssert.Contains(await invalid.Content.ReadAsStringAsync(Token), "browse.folderPictures");

        using var tooMany = await _client.PostAsJsonAsync(
            $"{V1}/browse/covers",
            new FolderCoversRequest(Enumerable.Repeat("Media", LibraryBrowser.MaxCoverPaths + 1).ToList()),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    private async Task<List<FolderCover>> PostCoversAsync(params string[] paths)
    {
        using var response = await _client.PostAsJsonAsync(
            $"{V1}/browse/covers",
            new FolderCoversRequest(paths),
            Json,
            Token
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<FolderCover>>(Json, Token))!;
    }

    [TestMethod]
    [DataRow($"{V1}/browse/siblings?path=..")]
    [DataRow($"{V1}/browse/siblings?path=Missing")]
    [DataRow($"{V1}/browse?path=..")]
    [DataRow($"{V1}/browse?path=C:/Windows")]
    [DataRow($"{V1}/browse?path=Missing")]
    [DataRow($"{V1}/files/raw/..%2F..%2Fwin.ini")]
    [DataRow($"{V1}/files/raw/Missing.txt")]
    [DataRow($"{V1}/files/animated/Missing.gif")]
    [DataRow($"{V1}/files/animated/..%2F..%2Fwin.ini")]
    [DataRow($"{V1}/thumbnails/RFile1.txt")]
    [DataRow($"{V1}/trickplay/files/Media/Holiday%202020.mp4")]
    [DataRow($"{V1}/trickplay/files/Media/Holiday%202020.mp4?sheet=0")]
    [DataRow($"{V1}/unknown")]
    public async Task InvalidOrMissingPathsReturnNotFound(string url)
    {
        using var response = await _client.GetAsync(url, Token);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TrickplayIsOffUntilEnabled()
    {
        var status = await GetAsync<TrickplayStatus>($"{V1}/trickplay");
        using var check = await _client.PostAsync($"{V1}/trickplay", null, Token);

        Assert.AreEqual(TrickplayState.Disabled, status.State);
        Assert.AreEqual(HttpStatusCode.Conflict, check.StatusCode);
    }

    [TestMethod]
    public async Task TrickplayActivityAndStatesAnswerWhileOff()
    {
        var activity = await GetAsync<TrickplayActivity>($"{V1}/trickplay/activity?limit=10");
        using var states = await _client.PostAsJsonAsync(
            $"{V1}/trickplay/states",
            new TrickplayStatesRequest(["Media/Holiday 2020.mp4"]),
            Json,
            Token
        );
        var body = await states.Content.ReadFromJsonAsync<TrickplayStates>(Json, Token);
        using var tooMany = await _client.PostAsJsonAsync(
            $"{V1}/trickplay/states",
            new TrickplayStatesRequest([.. Enumerable.Repeat("a.mp4", TrickplayService.MaxStatePaths + 1)]),
            Json,
            Token
        );

        Assert.AreEqual(TrickplayState.Disabled, activity.Status.State);
        Assert.IsEmpty(activity.Running);
        Assert.IsEmpty(activity.Pending);
        Assert.IsEmpty(activity.Recent);
        Assert.AreEqual(HttpStatusCode.OK, states.StatusCode);
        Assert.IsFalse(body!.Running);
        Assert.IsEmpty(body.Files);
        Assert.AreEqual(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [TestMethod]
    public async Task RawFilesSupportRangeRequests()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{V1}/files/raw/Media/Holiday%202020.mp4");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);

        using var response = await _client.SendAsync(request, Token);

        Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.AreEqual("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.HasCount(100, await response.Content.ReadAsByteArrayAsync(Token));
    }

    [TestMethod]
    public async Task RawFilesHandleSpecialCharacters()
    {
        using var response = await _client.GetAsync($"{V1}/files/raw/100%25_done%5B1%5D.txt?download=true", Token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("100%_done[1].txt", response.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [TestMethod]
    public async Task ImagesAreNotCachedByTheBrowser()
    {
        using var image = await _client.GetAsync($"{V1}/files/raw/Media/photo.jpg", Token);
        using var video = await _client.GetAsync($"{V1}/files/raw/Media/Episode%202.mkv", Token);

        Assert.IsTrue(image.Headers.CacheControl?.NoStore);
        Assert.IsNull(video.Headers.CacheControl);
    }

    [TestMethod]
    [DataRow("evil.html", "text/html")]
    [DataRow("evil.htm", "text/html")]
    [DataRow("evil.svg", "image/svg+xml")]
    [DataRow("evil.xml", "text/xml")]
    [DataRow("evil.xhtml", "application/xhtml+xml")]
    public async Task FilesThatCouldRunScriptsAreOnlyDownloaded(string name, string type)
    {
        File.WriteAllText(_library.FullPath(name), "<script>fetch('api/access/users')</script>");

        using var response = await _client.GetAsync($"{V1}/files/raw/{name}", Token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(type, response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.AreEqual(
            Otools.Net.Sfe.Api.Security.SecurityHeaders.FilePolicy,
            string.Join(';', response.Headers.GetValues("Content-Security-Policy"))
        );
        Assert.AreEqual("nosniff", string.Join(',', response.Headers.GetValues("X-Content-Type-Options")));
    }

    [TestMethod]
    public async Task SharedLinksOfPagesAreOnlyDownloaded()
    {
        File.WriteAllText(_library.FullPath("evil.html"), "<script>alert(1)</script>");
        using var created = await _client.PostAsJsonAsync($"{V1}/files/links", new FileLinkRequest("evil.html"), Token);
        var link = (await created.Content.ReadFromJsonAsync<Otools.Net.Sfe.Api.Security.FileLink>(Json, Token))!;

        using var visitor = ApiClients.Create(_factory);
        using var response = await visitor.GetAsync("/" + link.Url, Token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.AreEqual(
            Otools.Net.Sfe.Api.Security.SecurityHeaders.FilePolicy,
            string.Join(';', response.Headers.GetValues("Content-Security-Policy"))
        );
    }

    [TestMethod]
    public async Task TheServersOwnFilesAreNeverServedThroughALink()
    {
        // A link in the library to the folder holding the keys (forged cookies), the database and the settings.
        var link = _library.FullPath("escape");
        if (!LinkFolder(link, _library.BasePath))
            Assert.Inconclusive("Cannot make a folder link here.");
        try
        {
            var key = Directory
                .GetFiles(Path.Combine(_library.BasePath, "keys"), "*.xml")
                .Select(Path.GetFileName)
                .First();
            File.WriteAllText(Path.Combine(_library.BasePath, "settings.json"), "{}");

            using var keyFile = await _client.GetAsync($"{V1}/files/raw/escape/keys/{key}", Token);
            using var database = await _client.GetAsync($"{V1}/files/raw/escape/index.db", Token);
            using var settings = await _client.GetAsync($"{V1}/files/raw/escape/settings.json", Token);
            using var shared = await _client.PostAsJsonAsync(
                $"{V1}/files/links",
                new FileLinkRequest("escape/index.db"),
                Token
            );
            // Links still work for the library's own content.
            using var media = await _client.GetAsync($"{V1}/files/raw/escape/Root/RFile1.txt", Token);

            Assert.AreEqual(HttpStatusCode.NotFound, keyFile.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, database.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, settings.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, shared.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, media.StatusCode);
        }
        finally
        {
            // Not through the link: it leads back to the library's own folder.
            Directory.Delete(link);
        }
    }

    private static bool LinkFolder(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            )!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }

        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [TestMethod]
    [DataRow($"{V1}/files/raw/Media/photo.jpg")]
    [DataRow($"{V1}/files/raw/Media/Episode%202.mkv")]
    [DataRow($"{V1}/files/raw/RFile1.txt")]
    public async Task MediaAndTextStillOpenInTheBrowser(string url)
    {
        using var response = await _client.GetAsync(url, Token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNull(response.Content.Headers.ContentDisposition);
        Assert.AreEqual(
            Otools.Net.Sfe.Api.Security.SecurityHeaders.FilePolicy,
            string.Join(';', response.Headers.GetValues("Content-Security-Policy"))
        );
    }

    [TestMethod]
    [DataRow("/")]
    [DataRow($"{V1}/config")]
    [DataRow($"{V1}/missing")]
    public async Task EveryAnswerCarriesTheSecurityHeaders(string url)
    {
        using var visitor = ApiClients.Create(_factory);
        foreach (var client in new[] { _client, visitor })
        {
            using var response = await client.GetAsync(url, Token);

            Assert.AreEqual("nosniff", string.Join(',', response.Headers.GetValues("X-Content-Type-Options")), url);
            Assert.AreEqual("DENY", string.Join(',', response.Headers.GetValues("X-Frame-Options")), url);
            Assert.AreEqual("same-origin", string.Join(',', response.Headers.GetValues("Referrer-Policy")), url);
            Assert.AreEqual(
                Otools.Net.Sfe.Api.Security.SecurityHeaders.AppPolicy,
                string.Join(';', response.Headers.GetValues("Content-Security-Policy")),
                url
            );
        }
    }

    [TestMethod]
    public async Task TheWebClientHasNoInlineScripts()
    {
        // The app's policy runs only the app's own script files: an inline script would not run.
        using var response = await _client.GetAsync("/", Token);
        var html = await response.Content.ReadAsStringAsync(Token);

        Assert.DoesNotMatchRegex(
            new System.Text.RegularExpressions.Regex(
                @"<script(?![^>]*\ssrc=)[^>]*>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ),
            html
        );
        Assert.DoesNotMatchRegex(
            new System.Text.RegularExpressions.Regex(
                @"\son[a-z]+\s*=",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ),
            html
        );
    }

    [TestMethod]
    public async Task SearchPagesFollowTheCursor()
    {
        using var start = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest(""), Token);
        await WaitForScanAsync();

        var first = await GetAsync<SearchResults>($"{V1}/search?q=File&limit=3");
        var second = await GetAsync<SearchResults>(
            $"{V1}/search?q=File&limit=3&after={Uri.EscapeDataString(first.Next!)}"
        );

        Assert.IsTrue(first.HasMore);
        Assert.IsEmpty(first.Items.Select(i => i.Path).Intersect(second.Items.Select(i => i.Path)));
    }

    [TestMethod]
    public async Task SearchSortsByTheRequestedField()
    {
        using var start = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest(""), Token);
        await WaitForScanAsync();

        var first = await GetAsync<SearchResults>($"{V1}/search?q=&limit=3&sort=size&desc=true");
        var second = await GetAsync<SearchResults>(
            $"{V1}/search?q=&limit=3&sort=size&desc=true&after={Uri.EscapeDataString(first.Next!)}"
        );

        var sizes = first.Items.Concat(second.Items).Select(i => i.Size!.Value).ToArray();
        CollectionAssert.AreEqual(sizes.OrderDescending().ToArray(), sizes);
    }

    [TestMethod]
    public async Task NextBrowsePagesSeeFolderChanges()
    {
        var first = await GetAsync<DirectoryListing>($"{V1}/browse?path=Media&limit=2");
        var second = await GetAsync<DirectoryListing>($"{V1}/browse?path=Media&offset=2&limit=10");
        Assert.AreEqual(first.Total, second.Total);
        Assert.IsEmpty(first.Items.Select(i => i.Path).Intersect(second.Items.Select(i => i.Path)));

        // Adding a file changes the folder: the cached listing of the first page is not reused.
        _library.AddFile("Media/zz new.txt", 1);
        var third = await GetAsync<DirectoryListing>($"{V1}/browse?path=Media&offset=2&limit=10");
        Assert.AreEqual(first.Total + 1, third.Total);
        Assert.AreEqual("Media/zz new.txt", third.Items[^1].Path);
    }

    [TestMethod]
    public async Task ScanThenSearch()
    {
        using var start = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest(""), Token);
        Assert.AreEqual(HttpStatusCode.Accepted, start.StatusCode);

        var status = await WaitForScanAsync();
        Assert.AreEqual(ScanState.Completed, status.State);
        Assert.AreEqual(_library.DirectoryCount, status.DirectoriesScanned);
        Assert.AreEqual(0, status.Errors);
        Assert.AreEqual(0, status.DirectoriesQueued);

        var results = await GetAsync<SearchResults>($"{V1}/search?q=" + Uri.EscapeDataString("episode e:mkv"));
        CollectionAssert.AreEquivalent(
            new[] { "Media/Episode 2.mkv", "Media/Episode 10.mkv" },
            results.Items.Select(i => i.Path).ToArray()
        );
        Assert.IsFalse(results.HasMore);
    }

    [TestMethod]
    public async Task ScannedImagesAreListedAndFoundWithTheirResolution()
    {
        // A PNG's signature and header chunk: 1280×720.
        byte[] png =
        [
            0x89,
            0x50,
            0x4E,
            0x47,
            0x0D,
            0x0A,
            0x1A,
            0x0A,
            0,
            0,
            0,
            13,
            (byte)'I',
            (byte)'H',
            (byte)'D',
            (byte)'R',
            0,
            0,
            5,
            0,
            0,
            0,
            2,
            0xD0,
            8,
            2,
            0,
            0,
            0,
        ];
        await File.WriteAllBytesAsync(_library.FullPath("Media/screen.png"), png, Token);
        using (var start = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest(""), Token))
        {
            Assert.AreEqual(HttpStatusCode.Accepted, start.StatusCode);
        }

        Assert.AreEqual(ScanState.Completed, (await WaitForScanAsync()).State);

        var listed = (await GetAsync<DirectoryListing>($"{V1}/browse?path=Media")).Items.Single(i =>
            i.Name == "screen.png"
        );
        var found = (
            await GetAsync<SearchResults>($"{V1}/search?q=" + Uri.EscapeDataString("r:>=1280x720"))
        ).Items.Single();

        Assert.AreEqual((1280, 720), (listed.Width, listed.Height));
        Assert.AreEqual(("Media/screen.png", 1280, 720), (found.Path, found.Width, found.Height));
        Assert.IsEmpty((await GetAsync<SearchResults>($"{V1}/search?q=" + Uri.EscapeDataString("w:>1280"))).Items);
    }

    [TestMethod]
    public async Task ResolutionsAreShownAndReadUnlessTurnedOff()
    {
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        Assert.IsTrue(settings.Values.Browse.ShowResolution);
        Assert.IsTrue(settings.Values.Scan.ReadResolution);
        Assert.IsTrue((await GetAsync<ClientConfig>($"{V1}/config")).ShowResolution);

        settings.Values.Browse.ShowResolution = false;
        settings.Values.Scan.ReadResolution = false;
        using var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token);

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        Assert.IsFalse((await GetAsync<ClientConfig>($"{V1}/config")).ShowResolution);
        Assert.IsFalse((await GetAsync<SettingsView>($"{V1}/settings")).Values.Scan.ReadResolution);
    }

    [TestMethod]
    public async Task ResolutionMarksSendTheirThresholdsOnlyWhenOn()
    {
        var settings = await GetAsync<SettingsView>($"{V1}/settings");
        Assert.IsFalse(settings.Values.Browse.ResolutionMark);
        Assert.AreEqual(
            (1500, 720),
            (settings.Values.Browse.ImageHighResolutionPixels, settings.Values.Browse.VideoHighResolutionPixels)
        );
        Assert.IsNull((await GetAsync<ClientConfig>($"{V1}/config")).ResolutionMark);

        settings.Values.Browse.ResolutionMark = true;
        settings.Values.Browse.VideoHighResolutionPixels = 0;
        using (var refused = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        settings.Values.Browse.ImageHighResolutionPixels = 2000;
        settings.Values.Browse.VideoHighResolutionPixels = 1080;
        using (var saved = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token))
        {
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        }

        Assert.AreEqual(
            new ResolutionThresholds(2000, 1080),
            (await GetAsync<ClientConfig>($"{V1}/config")).ResolutionMark
        );

        settings.Values.Browse.ResolutionMark = false;
        using (var off = await _client.PutAsJsonAsync($"{V1}/settings", settings.Values, Json, Token))
        {
            Assert.AreEqual(HttpStatusCode.OK, off.StatusCode);
        }

        Assert.IsNull((await GetAsync<ClientConfig>($"{V1}/config")).ResolutionMark);
        Assert.AreEqual(1080, (await GetAsync<SettingsView>($"{V1}/settings")).Values.Browse.VideoHighResolutionPixels);
    }

    [TestMethod]
    public async Task CreateTagThenTagAFileAndFindIt()
    {
        using var created = await _client.PostAsJsonAsync(
            $"{V1}/tags",
            new TagInput("Vacation", Color: "teal", Aliases: ["Holiday trip"]),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var tag = (await created.Content.ReadFromJsonAsync<TagDetails>(Json, Token))!;

        using var tagged = await _client.PostAsJsonAsync(
            $"{V1}/file-tags",
            new FileTagChange(["Media/Holiday 2020.mp4"], Add: [tag.Id]),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.OK, tagged.StatusCode);

        var fileTags = await GetAsync<FileTags>(
            $"{V1}/file-tags?path=" + Uri.EscapeDataString("Media/Holiday 2020.mp4")
        );
        var listing = await GetAsync<DirectoryListing>($"{V1}/browse?path=Media");
        var results = await GetAsync<SearchResults>($"{V1}/search?q=" + Uri.EscapeDataString("t:\"holiday trip\""));
        var found = await GetAsync<TagSearchResults>($"{V1}/tags?q=vac");
        var config = await GetAsync<ClientConfig>($"{V1}/config");

        Assert.AreEqual("Vacation", fileTags.Tags.Single().Name);
        Assert.AreEqual("teal", listing.Items.Single(i => i.Name == "Holiday 2020.mp4").Tags!.Single().Color);
        Assert.IsEmpty(listing.Items.Single(i => i.Name == "Episode 2.mkv").Tags!);
        CollectionAssert.AreEqual(new[] { "Media/Holiday 2020.mp4" }, results.Items.Select(i => i.Path).ToArray());
        Assert.AreEqual(1, found.Matches.Single().FileCount);
        Assert.IsTrue(config.TagColors.Any(c => c.Slug == "teal"));
    }

    [TestMethod]
    public async Task TagsTheFilesOfAFolderTreeAndOfASearch()
    {
        using var folder = await _client.PostAsJsonAsync(
            $"{V1}/file-tags/scope",
            new ScopeTagChange("RepeatedDir", Recursive: true, Add: [BuiltInTags.Favorite]),
            Json,
            Token
        );
        var tree = await GetAsync<ScopeTags>($"{V1}/file-tags/scope?path=RepeatedDir&recursive=true");
        var own = await GetAsync<ScopeTags>($"{V1}/file-tags/scope?path=RepeatedDir");
        using var search = await _client.PostAsJsonAsync(
            $"{V1}/file-tags/scope",
            new ScopeTagChange("RepeatedDir", Query: "rdfile1", Remove: [BuiltInTags.Favorite]),
            Json,
            Token
        );
        var found = await GetAsync<ScopeTags>($"{V1}/file-tags/scope?path=&q=t%3Afavorite");
        using var missing = await _client.GetAsync($"{V1}/file-tags/scope?path=Nowhere", Token);
        using var noTag = await _client.PostAsJsonAsync(
            $"{V1}/file-tags/scope",
            new ScopeTagChange("Media"),
            Json,
            Token
        );

        Assert.AreEqual(HttpStatusCode.OK, folder.StatusCode);
        Assert.AreEqual(6, (await folder.Content.ReadFromJsonAsync<ScopeTagChangeResult>(Json, Token))!.Added);
        Assert.AreEqual((6, 6), (tree.Files, tree.Tags.Single().FileCount));
        Assert.AreEqual(2, own.Files);
        Assert.AreEqual(2, (await search.Content.ReadFromJsonAsync<ScopeTagChangeResult>(Json, Token))!.Removed);
        Assert.AreEqual(new TagScope("", Query: "t:favorite"), found.Scope);
        Assert.AreEqual(4, found.Files);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, noTag.StatusCode);
    }

    [TestMethod]
    public async Task SettingsChangeTheClientConfigRightAway()
    {
        var view = await GetAsync<SettingsView>($"{V1}/settings");
        var configured = view.Values.Browse.DefaultPageSize;
        view.Values.Browse.DefaultPageSize = 25;
        view.Values.Search.DefaultPageSize = 40;
        view.Values.Thumbnails.LargeImageKilobytes = 2048;

        using var saved = await _client.PutAsJsonAsync($"{V1}/settings", view.Values, Json, Token);
        var config = await GetAsync<ClientConfig>($"{V1}/config");
        view.Values.Browse.MaxPageSize = 0;
        using var invalid = await _client.PutAsJsonAsync($"{V1}/settings", view.Values, Json, Token);
        using var reset = await _client.DeleteAsync($"{V1}/settings", Token);
        var afterReset = await GetAsync<ClientConfig>($"{V1}/config");

        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        Assert.AreEqual(25, config.PageSize);
        Assert.AreEqual(40, config.SearchPageSize);
        Assert.AreEqual(2048 * 1024L, config.LargeImageSize);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        StringAssert.Contains(await invalid.Content.ReadAsStringAsync(Token), "browse.maxPageSize");
        Assert.AreEqual(HttpStatusCode.OK, reset.StatusCode);
        Assert.AreEqual(configured, afterReset.PageSize);
    }

    [TestMethod]
    public async Task TagEndpointsReportProblems()
    {
        using var invalid = await _client.PostAsJsonAsync($"{V1}/tags", new TagInput(" "), Json, Token);
        using var builtIn = await _client.DeleteAsync($"{V1}/tags/{BuiltInTags.Favorite}", Token);
        using var missing = await _client.GetAsync($"{V1}/tags/9999", Token);
        using var outside = await _client.PostAsJsonAsync(
            $"{V1}/file-tags",
            new FileTagChange(["../index.db"], Add: [BuiltInTags.Favorite]),
            Json,
            Token
        );
        using var noFile = await _client.GetAsync($"{V1}/file-tags?path=Media", Token);

        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, builtIn.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, noFile.StatusCode);
    }

    [TestMethod]
    public async Task TagManagerRoutesCreateEditAndDeleteATag()
    {
        // What the tag manager and editor dialogs do, through HTTP.
        using var created = await _client.PostAsJsonAsync(
            $"{V1}/tags",
            new TagInput("Kitty", Aliases: ["Cat"]),
            Json,
            Token
        );
        var tag = (await created.Content.ReadFromJsonAsync<TagDetails>(Json, Token))!;
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        Assert.AreEqual($"{V1}/tags/{tag.Id}", created.Headers.Location!.OriginalString);

        using var updated = await _client.PutAsJsonAsync(
            $"{V1}/tags/{tag.Id}",
            new TagInput("Cat", Color: "orange", ParentIds: [BuiltInTags.Meta], DisambiguationId: BuiltInTags.Meta),
            Json,
            Token
        );
        var edited = (await updated.Content.ReadFromJsonAsync<TagDetails>(Json, Token))!;
        Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode);
        Assert.AreEqual("Cat (Meta Tags)", edited.DisplayName);
        Assert.IsEmpty(edited.Aliases);

        using var deleted = await _client.DeleteAsync($"{V1}/tags/{tag.Id}", Token);
        using var again = await _client.DeleteAsync($"{V1}/tags/{tag.Id}", Token);
        using var gone = await _client.GetAsync($"{V1}/tags/{tag.Id}", Token);
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [TestMethod]
    public async Task TagEditsAnswerWithTheFieldInError()
    {
        var a = (
            await (
                await _client.PostAsJsonAsync($"{V1}/tags", new TagInput("A"), Json, Token)
            ).Content.ReadFromJsonAsync<TagDetails>(Json, Token)
        )!;
        var b = (
            await (
                await _client.PostAsJsonAsync($"{V1}/tags", new TagInput("B", ParentIds: [a.Id]), Json, Token)
            ).Content.ReadFromJsonAsync<TagDetails>(Json, Token)
        )!;

        using var loop = await _client.PutAsJsonAsync(
            $"{V1}/tags/{a.Id}",
            new TagInput("A", ParentIds: [b.Id]),
            Json,
            Token
        );
        using var color = await _client.PostAsJsonAsync($"{V1}/tags", new TagInput("C", Color: "plaid"), Json, Token);
        using var missing = await _client.PutAsJsonAsync($"{V1}/tags/9999", new TagInput("Ghost"), Json, Token);
        using var noBody = await _client.PostAsync(
            $"{V1}/tags",
            new StringContent("", System.Text.Encoding.UTF8, "application/json"),
            Token
        );

        Assert.AreEqual(HttpStatusCode.BadRequest, loop.StatusCode);
        // The client shows the first message of `errors` (api.service.ts).
        Assert.Contains("parentIds", await loop.Content.ReadAsStringAsync(Token));
        Assert.Contains("“B” already inherits from this tag", await loop.Content.ReadAsStringAsync(Token));
        Assert.Contains("\"color\"", await color.Content.ReadAsStringAsync(Token));
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, noBody.StatusCode);
    }

    [TestMethod]
    public async Task TagSearchLimitIsKeptInRange()
    {
        for (var i = 0; i < 3; i++)
        {
            using (await _client.PostAsJsonAsync($"{V1}/tags", new TagInput($"Tag {i}"), Json, Token)) { }
        }

        var zero = await GetAsync<TagSearchResults>($"{V1}/tags?limit=0");
        var negative = await GetAsync<TagSearchResults>($"{V1}/tags?limit=-5");
        var huge = await GetAsync<TagSearchResults>($"{V1}/tags?limit=1000000");
        var byDefault = await GetAsync<TagSearchResults>($"{V1}/tags?q=tag");

        Assert.HasCount(1, zero.Matches);
        Assert.HasCount(1, negative.Matches);
        Assert.HasCount(6, huge.Matches);
        // The three, and the built-in "Meta Tags".
        Assert.HasCount(4, byDefault.Matches);
        using var notANumber = await _client.GetAsync($"{V1}/tags?limit=lots", Token);
        Assert.AreEqual(HttpStatusCode.BadRequest, notANumber.StatusCode);
    }

    [TestMethod]
    public async Task ViewedFilesFeedTheSidebarUntilTheHistoryIsCleared()
    {
        using (
            await _client.PostAsJsonAsync(
                $"{V1}/file-tags",
                new FileTagChange(["Media/photo.jpg"], Add: [BuiltInTags.Favorite]),
                Json,
                Token
            )
        ) { }

        using var viewed = await _client.PostAsJsonAsync(
            $"{V1}/file-tags/viewed",
            new ViewedFile("Media/photo.jpg"),
            Json,
            Token
        );
        using var notAFile = await _client.PostAsJsonAsync(
            $"{V1}/file-tags/viewed",
            new ViewedFile("Media"),
            Json,
            Token
        );
        using var outside = await _client.PostAsJsonAsync(
            $"{V1}/file-tags/viewed",
            new ViewedFile("../index.db"),
            Json,
            Token
        );
        var sidebar = await GetAsync<TagSidebar>($"{V1}/tags/sidebar");

        Assert.AreEqual(HttpStatusCode.NoContent, viewed.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, notAFile.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.IsTrue(sidebar.HistoryEnabled);
        Assert.AreEqual("Favorite", sidebar.Viewed.Single().Tag.Name);
        Assert.AreEqual("Favorite", sidebar.Popular.Single().Tag.Name);

        using var cleared = await _client.DeleteAsync($"{V1}/tags/history", Token);
        Assert.AreEqual(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.IsEmpty((await GetAsync<TagSidebar>($"{V1}/tags/sidebar")).Viewed);
    }

    [TestMethod]
    public async Task TaggingASelectionAnswersWithEveryFilesTags()
    {
        using var response = await _client.PostAsJsonAsync(
            $"{V1}/file-tags",
            new FileTagChange(
                ["Media/photo.jpg", "RFile1.txt", "Media/photo.jpg"],
                Add: [BuiltInTags.Favorite, BuiltInTags.Archived],
                Remove: [BuiltInTags.Archived]
            ),
            Json,
            Token
        );
        var result = (await response.Content.ReadFromJsonAsync<FileTagChangeResult>(Json, Token))!;

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(
            new[] { "Media/photo.jpg", "RFile1.txt" },
            result.Files.Select(f => f.Path).ToArray()
        );
        Assert.AreEqual(4, result.Added);
        Assert.IsTrue(result.Files.All(f => f.Groups.Single().Category!.Name == "Meta Tags"));

        using var tooMany = await _client.PostAsJsonAsync(
            $"{V1}/file-tags",
            new FileTagChange(
                Enumerable.Repeat("RFile1.txt", TagService.MaxFilesPerChange + 1).ToArray(),
                Add: [BuiltInTags.Favorite]
            ),
            Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("\"paths\"", await tooMany.Content.ReadAsStringAsync(Token));
    }

    [TestMethod]
    public async Task EverySettingOfTheClientsSettingsDialogExists()
    {
        // settings-schema.ts (and the settings of a feature kept beside it, as tokens/token-settings.ts) names the
        // settings by their camelCase path in GET /api/settings `values`; a renamed or removed setting would show an
        // empty field and be sent back under a key the server ignores.
        var app = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "src", "Sfe.Web", "src", "app")
        );
        string[] schemaFiles =
        [
            Path.Combine(app, "settings", "settings-schema.ts"),
            Path.Combine(app, "tokens", "token-settings.ts"),
        ];
        if (schemaFiles.FirstOrDefault(file => !File.Exists(file)) is { } absent)
            Assert.Inconclusive($"Sources not found at {absent}.");
        var schemaText = string.Concat(
            await Task.WhenAll(schemaFiles.Select(file => File.ReadAllTextAsync(file, Token)))
        );
        var fields = System
            .Text.RegularExpressions.Regex.Matches(
                schemaText,
                // Prettier lays an entry out on one line or one property per line, a label in either quotes.
                @"\{\s*key: '(?<key>[\w.]+)',\s*label: (?:'[^']*'|""[^""]*""),\s*"
                    + @"type: '(?<type>\w+)'(?<numeric>,\s*numeric: true)?"
            )
            // A select of numbers (`numeric: true`) holds numbers.
            .Select(m =>
                (Key: m.Groups["key"].Value, Type: m.Groups["numeric"].Success ? "number" : m.Groups["type"].Value)
            )
            .Where(f => f.Type != "preset")
            .ToList();
        using var settings = JsonDocument.Parse(await _client.GetStringAsync($"{V1}/settings", Token));
        var values = settings.RootElement.GetProperty("values");

        Assert.IsGreaterThan(30, fields.Count, "The schema's fields were not found: the pattern may need updating.");
        Assert.Contains("security.accessTokens.maxLifetimeDays", fields.Select(f => f.Key).ToList());
        var missing = fields.Where(f => !TryGet(values, f.Key, out _)).Select(f => f.Key).ToList();
        Assert.IsEmpty(missing, "Settings unknown to the server: " + string.Join(", ", missing));

        foreach (var (key, type) in fields)
        {
            TryGet(values, key, out var value);
            var expected = type switch
            {
                "number" => [JsonValueKind.Number],
                "checkbox" => [JsonValueKind.True, JsonValueKind.False],
                "list" or "extensions" => [JsonValueKind.Array, JsonValueKind.Null],
                _ => new[] { JsonValueKind.String, JsonValueKind.Null },
            };
            Assert.Contains(
                value.ValueKind,
                expected,
                $"{key} is a {type} field but the server sends {value.ValueKind}."
            );
        }

        static bool TryGet(JsonElement element, string path, out JsonElement value)
        {
            value = element;
            foreach (var part in path.Split('.'))
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value))
                    return false;
            }

            return true;
        }
    }

    [TestMethod]
    public async Task ScanRejectsPathsOutsideTheLibrary()
    {
        using var response = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest("../.."), Token);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ServesTheWebClientAndOpenApi()
    {
        var html = await _client.GetStringAsync("/", Token);
        StringAssert.Contains(html, "<sfe-root");

        // The client's other pages, opened or reloaded at their own address.
        foreach (
            var page in new[] { "/admin?section=tasks&page=history", "/config?section=tags", "/player?path=a.mkv" }
        )
        {
            StringAssert.Contains(await _client.GetStringAsync(page, Token), "<sfe-root", page);
        }

        var openApi = await _client.GetStringAsync("/openapi/v1.json", Token);
        StringAssert.Contains(openApi, $"{V1}/search");
    }

    [TestMethod]
    public async Task ApiDocsNeverCallExternalServices()
    {
        // Swagger UI's pages are outside the API's versions (ApiRoutes.Docs).
        var html = await _client.GetStringAsync("/api/docs/index.html", Token);

        // Every script, style and icon of the Swagger UI page is relative, i.e. served by the app itself.
        var assets = System
            .Text.RegularExpressions.Regex.Matches(html, "(?:src|href)=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.IsNotEmpty(assets);
        foreach (var asset in assets)
        {
            Assert.DoesNotContain("//", asset, $"{asset} is not served by the application.");
            using var response = await _client.GetAsync("/api/docs/" + asset.TrimStart('.', '/'), Token);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, asset);
        }

        // No validator URL: Swagger UI would send the OpenAPI document to validator.swagger.io for its badge.
        var script = await _client.GetStringAsync("/api/docs/index.js", Token);
        StringAssert.Contains(script, "\"url\":\"../../openapi/v1.json\"");
        Assert.DoesNotContain("\"validatorUrl\":\"", script);
    }

    [TestMethod]
    public async Task FileTrackingRoutesListDuplicatesAndRelinkMissingFiles()
    {
        File.Copy(_library.FullPath("Media/photo.jpg"), _library.FullPath("photo copy.jpg"));
        using var created = await _client.PostAsJsonAsync($"{V1}/tags", new TagInput("Kept"), Json, Token);
        var tag = (await created.Content.ReadFromJsonAsync<TagDetails>(Json, Token))!;
        using (var start = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest(""), Token))
        {
            await WaitForScanAsync();
        }

        using (
            await _client.PostAsJsonAsync(
                $"{V1}/file-tags",
                new FileTagChange(["Media/Episode 2.mkv"], Add: [tag.Id]),
                Json,
                Token
            )
        ) { }

        var duplicates = await GetAsync<DuplicateResults>($"{V1}/duplicates");
        var group = Assert.ContainsSingle(duplicates.Groups);
        CollectionAssert.AreEquivalent(
            new[] { "Media/photo.jpg", "photo copy.jpg" },
            group.Files.Select(f => f.Path).ToArray()
        );
        using (var invalid = await _client.GetAsync($"{V1}/duplicates?path=../..", Token))
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        Assert.IsTrue(duplicates.CanRemove);
        using (
            var same = await _client.PostAsJsonAsync(
                $"{V1}/duplicates/remove",
                new RemoveDuplicateRequest("photo copy.jpg", "photo copy.jpg"),
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, same.StatusCode);
        }

        using (
            var removed = await _client.PostAsJsonAsync(
                $"{V1}/duplicates/remove",
                new RemoveDuplicateRequest("photo copy.jpg", "Media/photo.jpg"),
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.OK, removed.StatusCode);
            var body = (await removed.Content.ReadFromJsonAsync<RemovedDuplicate>(Json, Token))!;
            Assert.AreEqual("Media/photo.jpg", body.Kept.Path);
            Assert.IsNotNull(body.TrashId, "Moved to the trash");
        }
        Assert.IsFalse(File.Exists(_library.FullPath("photo copy.jpg")));

        // Deleted: kept as missing, then relinked by hand to another file.
        File.Delete(_library.FullPath("Media/Episode 2.mkv"));
        using (var start = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest(""), Token))
        {
            Assert.AreEqual(1, (await WaitForScanAsync()).Missing);
        }

        var missing = await GetAsync<MissingFileResults>($"{V1}/missing-files");
        var item = Assert.ContainsSingle(missing.Items);
        Assert.AreEqual(1, missing.Total);

        using (
            var bad = await _client.PostAsJsonAsync(
                $"{V1}/missing-files/{item.Id}/relink",
                new RelinkRequest("Nope.mkv"),
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        using (
            var relinked = await _client.PostAsJsonAsync(
                $"{V1}/missing-files/{item.Id}/relink",
                new RelinkRequest("Media/Episode 10.mkv"),
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.NoContent, relinked.StatusCode);
        }

        var tags = await GetAsync<FileTags>($"{V1}/file-tags?path=Media/Episode%2010.mkv");
        Assert.AreEqual(tag.Id, Assert.ContainsSingle(tags.Tags).Id);
        using (var gone = await _client.DeleteAsync($"{V1}/missing-files/{item.Id}", Token))
            Assert.AreEqual(HttpStatusCode.NotFound, gone.StatusCode);
        using var pass = await _client.PostAsync($"{V1}/missing-files/relink", null, Token);
        Assert.AreEqual(0, (await pass.Content.ReadFromJsonAsync<RelinkPassResult>(Json, Token))!.Remaining);
    }

    [TestMethod]
    public void WebClientHasNoExternalResources()
    {
        // tests/Sfe.Tests/ApiTests.cs -> repository root, wherever the build output goes.
        var src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "src"));
        var web = Path.Combine(src, "Sfe.Web");
        var wwwroot = Path.Combine(src, "Sfe.Api", "wwwroot");
        if (!Directory.Exists(web))
            Assert.Inconclusive($"Sources not found at {web}.");

        // The client's own sources, then the built client: the framework's bundled error messages name its
        // documentation site (text shown to developers, never requested). pdf.js parses links against the dummy bases
        // example.com and foo.bar (never fetched), names the XUL namespace (www.mozilla.org), and its pictures carry
        // the MPL's address (mozilla.org) in a comment. epub.js names the EPUB and Dublin Core namespaces
        // (www.idpf.org, purl.org), jszip and the bundler's CommonJS shim link their docs in error messages
        // (stuk.github.io, rolldown.rs). Ruffle (flash/ruffle/) links its site and its project (ruffle.rs, github.com)
        // in its error panel: links, not requests, which its frame can't even follow (no allow-popups). Hosts are
        // matched whole (the pattern doesn't take paths).
        var offenders = ExternalUrls(Path.Combine(web, "src"), [".ts", ".html", ".css"], [])
            .Concat(ExternalUrls(Path.Combine(web, "public"), [".html", ".js", ".css", ".svg"], []))
            .Concat(
                Directory.Exists(wwwroot)
                    ? ExternalUrls(
                        wwwroot,
                        [".html", ".js", ".css", ".svg"],
                        [
                            "angular.dev/",
                            "example.com",
                            "foo.bar",
                            "www.mozilla.org",
                            "mozilla.org",
                            "www.idpf.org",
                            "purl.org",
                            "stuk.github.io",
                            "rolldown.rs",
                            "ruffle.rs",
                            "github.com",
                        ]
                    )
                    : []
            )
            .ToList();

        Assert.IsEmpty(offenders, "External resources found:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// Absolute URLs (scripts, styles, fonts, fetches) in the files of <paramref name="root"/>, except XML namespaces,
    /// which are identifiers, not requests, and those starting with one of <paramref name="allowed"/>.
    /// </summary>
    private static IEnumerable<string> ExternalUrls(string root, string[] extensions, string[] allowed)
    {
        var external = new System.Text.RegularExpressions.Regex(
            @"(?<![\w.])(https?:)?//[a-z0-9-]+(\.[a-z0-9-]+)+/?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .SelectMany(f =>
                File.ReadLines(f)
                    .SelectMany(
                        (line, i) =>
                            external
                                .Matches(line)
                                .Select(m => m.Value)
                                .Where(url =>
                                    !url.Contains("www.w3.org", StringComparison.OrdinalIgnoreCase)
                                    && !allowed.Any(prefix =>
                                        Allowed(
                                            System.Text.RegularExpressions.Regex.Replace(url, "^(https?:)?//", ""),
                                            prefix
                                        )
                                    )
                                )
                                .Select(url => $"{Path.GetRelativePath(root, f)}:{i + 1}: {url}")
                    )
            );

        // `host/path/` allows what starts with it; a bare `host` that host only (not `host.evil.net`).
        static bool Allowed(string url, string prefix) =>
            prefix.Contains('/')
                ? url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                : url.TrimEnd('/').Equals(prefix, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task TasksCanBeStartedAndAreRecorded()
    {
        var overview = await GetAsync<TaskOverview>($"{V1}/tasks");
        Assert.HasCount(Enum.GetValues<BackgroundTaskKind>().Count(BackgroundTasks.IsListed), overview.Tasks);
        Assert.AreEqual(30, overview.LogRetentionDays);
        Assert.IsFalse(overview.Tasks.Single(t => t.Task == BackgroundTaskKind.Thumbnails).CanSchedule);

        using (var cancel = await _client.PostAsync($"{V1}/tasks/scan/cancel", null, Token))
            Assert.AreEqual(HttpStatusCode.Conflict, cancel.StatusCode);
        using (var unknown = await _client.PostAsync($"{V1}/tasks/1/start", null, Token))
            Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);

        using (var start = await _client.PostAsync($"{V1}/tasks/scan/start?path=Media", null, Token))
        {
            Assert.AreEqual(HttpStatusCode.Accepted, start.StatusCode);
        }

        Assert.AreEqual(ScanState.Completed, (await WaitForScanAsync()).State);

        var runs = await GetAsync<TaskRunPage>($"{V1}/tasks/runs?task=scan");
        var run = runs.Runs.Single();
        Assert.AreEqual(TaskOutcome.Completed, run.Outcome);
        Assert.AreEqual("Media", run.Path);
        var details = await GetAsync<TaskRunDetails>($"{V1}/tasks/runs/{run.Id}");
        Assert.IsNotEmpty(details.Log);

        var last = (await GetAsync<TaskOverview>($"{V1}/tasks"))
            .Tasks.Single(t => t.Task == BackgroundTaskKind.Scan)
            .LastRun;
        Assert.AreEqual(run.Id, last?.Id);

        using (var clear = await _client.DeleteAsync($"{V1}/tasks/runs", Token))
            Assert.AreEqual(HttpStatusCode.NoContent, clear.StatusCode);
        Assert.IsEmpty((await GetAsync<TaskRunPage>($"{V1}/tasks/runs")).Runs);
    }

    [TestMethod]
    public async Task SchedulesCanBeCreatedChangedAndDeleted()
    {
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var input = new TaskScheduleInput(
            BackgroundTaskKind.Scan,
            "Media",
            ScheduleKind.Weekly,
            start,
            0,
            ScheduleCalculator.AllDays,
            true
        );

        using var preview = await _client.PostAsJsonAsync($"{V1}/tasks/schedules/preview", input, Json, Token);
        var runs = (await preview.Content.ReadFromJsonAsync<SchedulePreview>(Json, Token))!.Runs;
        Assert.HasCount(TaskScheduleService.PreviewCount, runs);
        // A day apart on the server's clock (23 or 25 hours across a change to or from summer time).
        Assert.AreEqual(
            TimeSpan.FromDays(1),
            TimeZoneInfo.ConvertTime(runs[1], TimeZoneInfo.Local).DateTime
                - TimeZoneInfo.ConvertTime(runs[0], TimeZoneInfo.Local).DateTime
        );

        using var created = await _client.PostAsJsonAsync($"{V1}/tasks/schedules", input, Json, Token);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var schedule = (await created.Content.ReadFromJsonAsync<TaskScheduleView>(Json, Token))!;
        Assert.AreEqual(runs[0], schedule.NextRun);

        using (
            var invalid = await _client.PutAsJsonAsync(
                $"{V1}/tasks/schedules/{schedule.Id}",
                input with
                {
                    Days = 0,
                },
                Json,
                Token
            )
        )
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using (
            var off = await _client.PutAsJsonAsync(
                $"{V1}/tasks/schedules/{schedule.Id}",
                input with
                {
                    Enabled = false,
                },
                Json,
                Token
            )
        )
        {
            Assert.IsNull((await off.Content.ReadFromJsonAsync<TaskScheduleView>(Json, Token))!.NextRun);
        }

        Assert.AreEqual(schedule.Id, (await GetAsync<TaskOverview>($"{V1}/tasks")).Schedules.Single().Id);
        using (var deleted = await _client.DeleteAsync($"{V1}/tasks/schedules/{schedule.Id}", Token))
        {
            Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        Assert.IsEmpty((await GetAsync<TaskOverview>($"{V1}/tasks")).Schedules);
    }

    [TestMethod]
    public async Task RemovedFolderCanBeExcludedFromScans()
    {
        using (var start = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest(""), Token))
        {
            await WaitForScanAsync();
        }

        var folder = await GetAsync<IndexedFolder>($"{V1}/index/folder?path=Media");
        Assert.IsTrue(folder.Indexed);
        Assert.IsFalse(folder.Excluded);
        Assert.IsGreaterThan(0, folder.Files);

        using var removed = await _client.DeleteAsync($"{V1}/index/folder?path=Media&exclude=true", Token);
        var result = (await removed.Content.ReadFromJsonAsync<RemovedFromIndex>(Json, Token))!;
        Assert.AreEqual(folder.Files, result.Files);
        Assert.IsTrue(result.Excluded);
        // A setting of the library: its folders mean nothing to the server or another library.
        CollectionAssert.Contains(
            (await GetAsync<SettingsView>($"{V1}/settings?level=tenant")).Values.Scan.ExcludedFolders,
            "Media"
        );
        CollectionAssert.DoesNotContain(
            (await GetAsync<SettingsView>($"{V1}/settings?level=server")).Values.Scan.ExcludedFolders,
            "Media"
        );

        // A scan of the whole library leaves it out.
        using (var start = await _client.PostAsJsonAsync($"{V1}/scan", new ScanRequest(""), Token))
        {
            await WaitForScanAsync();
        }

        var after = await GetAsync<IndexedFolder>($"{V1}/index/folder?path=Media");
        Assert.IsFalse(after.Indexed);
        Assert.AreEqual(0, after.Files);
        Assert.IsTrue(after.Excluded);

        using var outside = await _client.DeleteAsync($"{V1}/index/folder?path=../..", Token);
        Assert.AreEqual(HttpStatusCode.BadRequest, outside.StatusCode);
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private async Task<ScanStatus> WaitForScanAsync()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var status = await GetAsync<ScanStatus>($"{V1}/scan");
            if (status.State != ScanState.Running)
                return status;
            await Task.Delay(50, Token);
        }

        throw new AssertFailedException("Scan did not finish in time.");
    }

    private async Task<T> GetAsync<T>(string url) =>
        await _client.GetFromJsonAsync<T>(url, Json, Token) ?? throw new AssertFailedException($"{url} returned null.");
}
