using Microsoft.Extensions.Options;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Search;
using Sfe.Core.Settings;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Settings changed from the web client: validation, live application, persistence and reset.</summary>
[TestClass]
public sealed class SettingsTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    private SettingsService Settings => _services.Get<SettingsService>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, s => s["Sfe:Scan:IgnoredDirectories:0"] = "SpecialDir");
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task SavedSettingsApplyRightAwayAndReplaceConfiguredLists()
    {
        var settings = Settings.Get().Values;
        Assert.IsFalse(Settings.Get().Customized);
        CollectionAssert.AreEqual(new[] { "SpecialDir" }, settings.Scan.IgnoredDirectories);

        settings.Browse.DefaultPageSize = 2;
        settings.Scan.IgnoredDirectories = ["RepeatedDir", " ", "repeateddir"];
        settings.FileTypes.Videos = [".MP4"];
        settings.ShowHiddenItems = true;
        settings.Trickplay.Folders = ["/Media/", "Media", " "];
        var (view, errors) = await Settings.SaveAsync(settings, TestContext.CancellationToken);

        Assert.IsNull(errors);
        Assert.IsTrue(view!.Customized);
        Assert.AreEqual(2, view.Values.Browse.DefaultPageSize);
        // A saved list replaces the configured one instead of merging with it item by item.
        CollectionAssert.AreEqual(new[] { "RepeatedDir" }, view.Values.Scan.IgnoredDirectories);
        CollectionAssert.AreEqual(new[] { "mp4" }, view.Values.FileTypes.Videos);
        CollectionAssert.AreEqual(new[] { "Media" }, view.Values.Trickplay.Folders);

        var classifier = _services.Get<MediaClassifier>();
        Assert.AreEqual(ItemKind.Video, classifier.Classify(".mp4"));
        Assert.AreEqual(ItemKind.File, classifier.Classify(".mkv"));
        Assert.IsTrue(_services.Get<LibraryBrowser>().EnumerationOptions.AttributesToSkip == 0);

        await ScanAsync();
        Assert.IsEmpty(await SearchNamesAsync("RDFile"), "RepeatedDir is now ignored.");
        CollectionAssert.AreEqual(new[] { "SDEnglish.con" }, await SearchNamesAsync("SDEnglish"), "SpecialDir is no longer ignored.");
    }

    [TestMethod]
    public async Task SettingsSurviveARestartAndResetRestoresTheConfiguration()
    {
        var settings = Settings.Get().Values;
        settings.Search.DefaultPageSize = 7;
        settings.Tags.SidebarCount = 3;
        settings.Player.SeekSeconds = 30;
        settings.Player.ExternalName = "Infuse";
        settings.Player.ExternalUrl = " infuse://x-callback-url/play?url={encodedUrl} ";
        await Settings.SaveAsync(settings, TestContext.CancellationToken);

        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library);
        Assert.AreEqual(7, Settings.Get().Values.Search.DefaultPageSize);
        Assert.AreEqual(3, Settings.Get().Values.Tags.SidebarCount);
        Assert.AreEqual(30, Settings.Get().Values.Player.SeekSeconds);
        Assert.AreEqual("infuse://x-callback-url/play?url={encodedUrl}", Settings.Get().Values.Player.ExternalUrl);

        var reset = Settings.Reset();
        Assert.IsFalse(reset.Customized);
        Assert.AreEqual(120, reset.Values.Search.DefaultPageSize);
        Assert.AreEqual(10, reset.Values.Player.SeekSeconds);
        Assert.AreEqual("vlc://{url}", reset.Values.Player.ExternalUrl);
        Assert.IsFalse(File.Exists(_library.SettingsPath));
    }

    [TestMethod]
    public async Task TheThemeIsConfiguredSavedByNameAndValidated()
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, s => s["Sfe:Appearance:Theme"] = "light");
        var settings = Settings.Get().Values;
        Assert.AreEqual(Theme.Light, settings.Appearance.Theme);

        settings.Appearance.Theme = Theme.Dark;
        await Settings.SaveAsync(settings, TestContext.CancellationToken);
        StringAssert.Contains(await File.ReadAllTextAsync(_services.Get<SettingsStore>().FilePath, TestContext.CancellationToken), "\"Dark\"");

        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library);
        Assert.AreEqual(Theme.Dark, Settings.Get().Values.Appearance.Theme);

        settings.Appearance.Theme = (Theme)42;
        var (_, errors) = await Settings.SaveAsync(settings, TestContext.CancellationToken);
        Assert.IsNotNull(errors);
        Assert.IsTrue(errors.ContainsKey("appearance.theme"));
    }

    [TestMethod]
    public async Task KeysMissingFromTheFileKeepTheirConfiguredValue()
    {
        // A file written before a setting existed, edited by hand.
        await File.WriteAllTextAsync(_library.SettingsPath, """{ "browse": { "defaultPageSize": 3 } }""", TestContext.CancellationToken);
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library);

        var values = Settings.Get().Values;
        Assert.AreEqual(3, values.Browse.DefaultPageSize);
        Assert.AreEqual(1000, values.Browse.MaxPageSize);
        CollectionAssert.Contains(values.FileTypes.Videos, "mkv");
        Assert.AreEqual(10, values.Player.SeekSeconds);
    }

    [TestMethod]
    public async Task TheThumbnailCacheFolderIsOnlySetInTheConfiguration()
    {
        // A file saved when the cache folder was still on the Settings page.
        await File.WriteAllTextAsync(_library.SettingsPath,
            """{ "thumbnails": { "cacheDirectory": "C:/Elsewhere", "width": 300 } }""", TestContext.CancellationToken);
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library);

        var options = _services.Get<Microsoft.Extensions.Options.IOptionsMonitor<Sfe.Core.Options.SfeOptions>>().CurrentValue;
        Assert.AreEqual(_library.ThumbnailPath, options.Thumbnails.CacheDirectory);
        Assert.AreEqual(300, options.Thumbnails.Width);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(Settings.Get(), SettingsStore.Json).Contains("cacheDirectory"));
    }

    [TestMethod]
    public async Task ConfiguredFileTypesReplaceTheDefaults()
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, s => s["Sfe:FileTypes:Images:0"] = "jpg");

        var fileTypes = Settings.Get().Values.FileTypes;
        CollectionAssert.AreEqual(new[] { "jpg" }, fileTypes.Images, "Configured: no default added.");
        CollectionAssert.AreEqual(Sfe.Core.Options.FileTypeOptions.DefaultVideos, fileTypes.Videos, "Not configured: defaults.");
        Assert.AreEqual(ItemKind.File, _services.Get<MediaClassifier>().Classify(".png"));
    }

    [TestMethod]
    public async Task AnUnreadableFileIsIgnored()
    {
        await File.WriteAllTextAsync(_library.SettingsPath, "{ not json", TestContext.CancellationToken);
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library);

        Assert.IsFalse(Settings.Get().Customized);
        Assert.AreEqual(120, Settings.Get().Values.Browse.DefaultPageSize);
    }

    [TestMethod]
    public async Task MoreTextExtensionsAreCleanedAndNeverMedia()
    {
        var settings = Settings.Get().Values;
        Assert.IsEmpty(settings.FileTypes.Text);

        settings.FileTypes.Text = [" .DAT", "out", "dat"];
        var (saved, errors) = await Settings.SaveAsync(settings, TestContext.CancellationToken);
        Assert.IsNull(errors);
        CollectionAssert.AreEqual(new[] { "dat", "out" }, saved!.Values.FileTypes.Text);

        settings.FileTypes.Text = ["mp4"];
        (_, errors) = await Settings.SaveAsync(settings, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new[] { "fileTypes.text" }, errors!.Keys.ToArray());

        settings.FileTypes.Text = ["a b"];
        (_, errors) = await Settings.SaveAsync(settings, TestContext.CancellationToken);
        CollectionAssert.AreEqual(new[] { "fileTypes.text" }, errors!.Keys.ToArray());
    }

    [TestMethod]
    public async Task InvalidSettingsAreRejectedPerField()
    {
        var settings = Settings.Get().Values;
        settings.Browse.MaxPageSize = 50;
        settings.Browse.DefaultPageSize = 60;
        settings.Search.MaxPageSize = 0;
        settings.Scan.Parallelism = 0;
        settings.Scan.IgnoredFiles = ["a/b.txt"];
        settings.FileTypes.Audio = ["mp4"];
        settings.FileTypes.Images = ["j pg"];
        settings.Thumbnails.Width = 10;
        settings.Thumbnails.LargeImageKilobytes = -1;
        settings.DataServer = "ftp://server";
        settings.Tags.SidebarCount = 50;
        settings.Trickplay.Folders = ["Media", "../Elsewhere"];
        settings.Trickplay.Columns = 0;
        settings.Player.SeekSeconds = 0;
        settings.Player.ExternalUrl = " javascript:alert(1)//{url}";

        var (view, errors) = await Settings.SaveAsync(settings, TestContext.CancellationToken);

        Assert.IsNull(view);
        CollectionAssert.AreEquivalent(
            new[] { "browse.defaultPageSize", "search.maxPageSize", "search.defaultPageSize", "scan.parallelism", "scan.ignoredFiles", "fileTypes", "fileTypes.images", "thumbnails.width", "thumbnails.largeImageKilobytes", "dataServer", "tags.sidebarCount", "trickplay.folders", "trickplay.columns", "player.seekSeconds", "player.externalUrl" },
            errors!.Keys.ToArray());
        Assert.IsFalse(File.Exists(_library.SettingsPath));
    }

    [TestMethod]
    [DataRow(" http://nas:8080/media// ", "http://nas:8080/media")]
    [DataRow("http://nas/files/{url}/", "http://nas/files/{url}/")]
    [DataRow(" https://nas/get?file={encodedUrl}&raw=1 ", "https://nas/get?file={encodedUrl}&raw=1")]
    [DataRow("   ", null)]
    public async Task TheDataServerIsABaseAddressOrATemplate(string value, string? saved)
    {
        var settings = Settings.Get().Values;
        settings.DataServer = value;

        var (view, errors) = await Settings.SaveAsync(settings, TestContext.CancellationToken);

        Assert.IsNull(errors);
        Assert.AreEqual(saved, view!.Values.DataServer);
        Assert.AreEqual(saved, _services.Get<IOptionsMonitor<SfeOptions>>().CurrentValue.DataServer);
    }

    [TestMethod]
    [DataRow("{url}")]
    [DataRow("ftp://nas/{url}")]
    [DataRow("javascript:alert(1)//{encodedUrl}")]
    [DataRow("nas/{url}")]
    public async Task ATemplateMustStillBeAnHttpAddress(string value)
    {
        var settings = Settings.Get().Values;
        settings.DataServer = value;

        var (view, errors) = await Settings.SaveAsync(settings, TestContext.CancellationToken);

        Assert.IsNull(view);
        CollectionAssert.AreEqual(new[] { "dataServer" }, errors!.Keys.ToArray());
    }

    [TestMethod]
    public async Task TurningHistoryOffStopsRecordingAndClearingForgetsIt()
    {
        var tags = _services.Get<TagService>();
        var (tagged, _) = await tags.ChangeFileTagsAsync(new FileTagChange(["RFile1.txt"], Add: [BuiltInTags.Favorite]), TestContext.CancellationToken);
        Assert.IsNotNull(tagged);
        await tags.RecordViewedAsync("RFile1.txt", TestContext.CancellationToken);
        Assert.HasCount(1, (await tags.GetSidebarAsync(TestContext.CancellationToken)).Viewed);

        var settings = Settings.Get().Values;
        settings.Tags.RecordHistory = false;
        await Settings.SaveAsync(settings, TestContext.CancellationToken);
        await SearchNamesAsync("t:favorite");
        var off = await tags.GetSidebarAsync(TestContext.CancellationToken);

        Assert.IsFalse(off.HistoryEnabled);
        Assert.IsEmpty(off.Searched);
        Assert.IsEmpty(off.Viewed);

        settings.Tags.RecordHistory = true;
        await Settings.SaveAsync(settings, TestContext.CancellationToken);
        var on = await tags.GetSidebarAsync(TestContext.CancellationToken);
        Assert.IsEmpty(on.Searched, "Nothing was recorded while history was off.");
        Assert.HasCount(1, on.Viewed, "History kept from before is shown again.");

        Assert.AreEqual(1, await tags.ClearHistoryAsync(TestContext.CancellationToken));
        Assert.IsEmpty((await tags.GetSidebarAsync(TestContext.CancellationToken)).Viewed);
    }

    private async Task ScanAsync()
    {
        var progress = new ScanProgress();
        await _services.Get<DirectoryIndexer>().IndexAsync(_library.Root, true, progress, TestContext.CancellationToken);
        Assert.AreEqual(0, progress.Errors);
    }

    private async Task<string[]> SearchNamesAsync(string query) =>
        (await _services.Get<FileSearchService>().SearchAsync(query, "", null, 200, cancellationToken: TestContext.CancellationToken))!.Items.Select(i => i.Name).ToArray();
}
