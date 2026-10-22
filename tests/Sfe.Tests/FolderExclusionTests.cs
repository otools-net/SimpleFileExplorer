using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Settings;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Folders kept out of scans, trickplay and thumbnails (<see cref="FolderExclusions"/>).</summary>
[TestClass]
public sealed class FolderExclusionTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private SfeOptions Options => _services.Get<IOptionsMonitor<SfeOptions>>().CurrentValue;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public void AFolderIsExcludedItselfOrThroughTheNearestListedParent()
    {
        var options = new SfeOptions();
        options.Scan.ExcludedFolders = ["Media", "Media/Old", "Other/"];
        options.Thumbnails.ExcludedFolders = ["Photos/Raw"];
        var exclusions = FolderExclusions.Of(options);

        Assert.AreEqual("Media", exclusions.Through(ExclusionKind.Scan, "Media"));
        Assert.AreEqual("Media/Old", exclusions.Through(ExclusionKind.Scan, "Media/Old/2019"), "the nearest listed folder");
        Assert.AreEqual("Media", exclusions.Through(ExclusionKind.Scan, "Media/New"));
        Assert.AreEqual("Other", exclusions.Through(ExclusionKind.Scan, "Other/x"), "listed paths are normalised");
        Assert.IsNull(exclusions.Through(ExclusionKind.Scan, "Mediathek"), "a name starting the same is another folder");
        Assert.IsNull(exclusions.Through(ExclusionKind.Scan, ""));
        Assert.IsNull(exclusions.Through(ExclusionKind.Trickplay, "Media"));

        Assert.AreEqual(new FolderExclusionState("Media", null, null), exclusions.At("Media/a"));
        Assert.AreEqual(new FolderExclusionState(null, null, "Photos/Raw"), exclusions.Child(FolderExclusionState.None, "Photos/Raw"));
        Assert.AreEqual(new FolderExclusionState("Media", null, null), exclusions.Child(exclusions.At("Media"), "Media/a"));
        Assert.IsTrue(exclusions.At("Photos").IsNone);

        Assert.IsTrue(FolderExclusions.Of(new SfeOptions()).IsEmpty);
        // A list replaced on the same options is read again.
        options.Thumbnails.ExcludedFolders = [];
        Assert.IsNull(FolderExclusions.Of(options).Through(ExclusionKind.Thumbnails, "Photos/Raw"));
    }

    [TestMethod]
    public void ListingsMarkTheFolderAndItsExcludedSubFolders()
    {
        var options = new SfeOptions();
        options.Trickplay.ExcludedFolders = ["Media/Trailers"];
        options.Scan.ExcludedFolders = ["Media"];
        var listing = new DirectoryListing("Media", "Media", "", [], [
            new LibraryItem("Trailers", "Media/Trailers", ItemKind.Directory, null, null, null),
            new LibraryItem("Shows", "Media/Shows", ItemKind.Directory, null, null, null),
            new LibraryItem("clip.mp4", "Media/clip.mp4", ItemKind.Video, ".mp4", 1, null),
        ], 0, 10, 3, 2, 1);

        var marked = FolderExclusions.Of(options).Mark(listing);

        Assert.AreEqual(new FolderExclusionState("Media", null, null), marked.Exclusions);
        Assert.AreEqual(new FolderExclusionState("Media", "Media/Trailers", null), marked.Items[0].Excluded);
        Assert.AreEqual(new FolderExclusionState("Media", null, null), marked.Items[1].Excluded);
        Assert.IsNull(marked.Items[2].Excluded, "files carry nothing");
        Assert.AreSame(listing, FolderExclusions.Of(new SfeOptions()).Mark(listing), "nothing excluded: unchanged");
    }

    [TestMethod]
    public async Task ScansKeepWhatTheIndexHasOfAnExcludedFolder()
    {
        await ScanAsync("");
        var tag = (await _services.Get<TagService>().CreateAsync(new TagInput("Kept"), Token)).Tag!.Id;
        var (tagged, errors) = await _services.Get<TagService>().ChangeFileTagsAsync(new FileTagChange(["Media/photo.jpg"], Add: [tag]), Token);
        Assert.IsNotNull(tagged, errors is null ? null : string.Join(" ", errors.SelectMany(e => e.Value)));

        var keys = await _services.Get<SettingsService>().SetExclusionsAsync("Media", new Dictionary<ExclusionKind, bool> { [ExclusionKind.Scan] = true }, cancellationToken: Token);
        CollectionAssert.AreEqual(new[] { "scan.excludedFolders" }, keys.ToArray());
        CollectionAssert.AreEqual(new[] { "Media" }, Options.Scan.ExcludedFolders);

        // Changed on disk since: a scan of the library, or of the folder itself, leaves the index as it was.
        _library.AddFile("Media/new.txt", 10);
        File.Delete(_library.FullPath("Media/Episode 2.mkv"));
        _library.AddFile("Media/Sub/deeper.txt", 10);
        await ScanAsync("");
        await ScanAsync("Media/Sub");

        await using var db = await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);
        Assert.IsTrue(await db.Directories.AnyAsync(d => d.FullName == _library.FullPath("Media"), Token));
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("Media/Episode 2.mkv"), Token), "frozen, not refreshed");
        Assert.IsFalse(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("Media/new.txt"), Token));
        Assert.IsFalse(await db.Directories.AnyAsync(d => d.FullName == _library.FullPath("Media/Sub"), Token));
        Assert.AreEqual(0, await db.MissingFiles.CountAsync(Token));
        CollectionAssert.AreEqual(new[] { tag }, (await _services.Get<TagService>().GetFileTagsAsync("Media/photo.jpg", Token))!.Tags.Select(t => t.Id).ToArray());

        // Let in again: the next scan catches up.
        await _services.Get<SettingsService>().SetExclusionsAsync("Media", new Dictionary<ExclusionKind, bool> { [ExclusionKind.Scan] = false }, cancellationToken: Token);
        Assert.IsEmpty(Options.Scan.ExcludedFolders);
        await ScanAsync("");
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("Media/new.txt"), Token));
        Assert.IsFalse(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("Media/Episode 2.mkv"), Token));
    }

    [TestMethod]
    public async Task ExclusionsAreSavedOnlyWhenTheyChange()
    {
        var settings = _services.Get<SettingsService>();
        var both = new Dictionary<ExclusionKind, bool> { [ExclusionKind.Trickplay] = true, [ExclusionKind.Thumbnails] = true };

        CollectionAssert.AreEquivalent(new[] { "trickplay.excludedFolders", "thumbnails.excludedFolders" }, (await settings.SetExclusionsAsync("Media", both, cancellationToken: Token)).ToArray());
        Assert.IsEmpty(await settings.SetExclusionsAsync("Media", both, cancellationToken: Token));
        CollectionAssert.AreEqual(new[] { "Media" }, Options.Trickplay.ExcludedFolders);
        CollectionAssert.AreEqual(new[] { "Media" }, Options.Thumbnails.ExcludedFolders);
        Assert.IsEmpty(Options.Scan.ExcludedFolders);

        await settings.SetExclusionsAsync("Media", new Dictionary<ExclusionKind, bool> { [ExclusionKind.Thumbnails] = false }, cancellationToken: Token);
        CollectionAssert.AreEqual(new[] { "Media" }, Options.Trickplay.ExcludedFolders);
        Assert.IsEmpty(Options.Thumbnails.ExcludedFolders);
    }

    [TestMethod]
    public async Task ExcludedFoldersGetNoNewThumbnailsButKeepTheirs()
    {
        await using var services = await CoreServices.CreateAsync(_library, s => s["Sfe:Thumbnails:ExcludedFolders:0"] = "Media");
        var thumbnails = services.Get<ThumbnailService>();

        Assert.IsNull(await thumbnails.GetAsync("Media/photo.jpg", Token));
        var cache = thumbnails.GetCachePath(_library.FullPath("Media/photo.jpg"));
        Assert.IsFalse(File.Exists(cache), "nothing was rendered");

        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        await File.WriteAllBytesAsync(cache, [0xFF, 0xD8, 0xFF], Token);
        Assert.AreEqual(cache, await thumbnails.GetAsync("Media/photo.jpg", Token), "one made before is still shown");
    }

    private async Task ScanAsync(string path)
    {
        var scan = _services.Get<ScanCoordinator>();
        Assert.IsTrue(scan.TryStart(path));
        await scan.Completion!;
    }
}
