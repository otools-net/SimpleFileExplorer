using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Search;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Tag hierarchy, file tagging and tag search, after TagStudio's behavior (docs/tags.md, docs/search.md).</summary>
[TestClass]
public sealed class TaggingTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    private TagService Tags => _services.Get<TagService>();

    private ScopeTagger Scope => _services.Get<ScopeTagger>();

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
    public async Task NewLibrariesHaveTheBuiltInTags()
    {
        var all = (await Tags.SearchAsync(null, 100, TestContext.CancellationToken)).Matches;

        CollectionAssert.AreEqual(new[] { "Archived", "Favorite", "Meta Tags" }, all.Select(t => t.Name).ToArray());
        var archived = all.Single(t => t.Id == BuiltInTags.Archived);
        Assert.IsTrue(archived.IsHidden);
        Assert.IsTrue(archived.IsBuiltIn);
        CollectionAssert.AreEqual(new[] { "Archive" }, archived.Aliases.ToArray());
        Assert.AreEqual("Meta Tags", archived.Parents.Single().Name);
        Assert.IsTrue(all.Single(t => t.Id == BuiltInTags.Meta).IsCategory);
    }

    [TestMethod]
    public async Task SearchingForAParentFindsFilesTaggedWithItsDescendants()
    {
        // docs/tags.md: Jinx < Arcane < League of Legends; Zander < Arcane.
        var lol = await CreateAsync(new TagInput("League of Legends", Shorthand: "LoL", Aliases: ["League"]));
        var arcane = await CreateAsync(new TagInput("Arcane", ParentIds: [lol.Id]));
        var jinx = await CreateAsync(new TagInput("Jinx Piltover", Shorthand: "Jinx", Aliases: ["Jinxy"], ParentIds: [lol.Id, arcane.Id]));
        var zander = await CreateAsync(new TagInput("Zander Zanderson", ParentIds: [arcane.Id]));

        await TagAsync(["Media/photo.jpg"], jinx.Id);
        await TagAsync(["Media/Episode 2.mkv"], zander.Id);
        await TagAsync(["Media/Episode 10.mkv"], lol.Id);

        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "Episode 2.mkv" }, await SearchNamesAsync("t:Arcane"));
        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "Episode 2.mkv", "Episode 10.mkv" }, await SearchNamesAsync("t:\"League of Legends\""));
        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "Episode 2.mkv", "Episode 10.mkv" }, await SearchNamesAsync("tag:lol"));
        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "Episode 2.mkv", "Episode 10.mkv" }, await SearchNamesAsync("t:League_of_Legends"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:jinxy"));
        CollectionAssert.AreEqual(new[] { "Episode 2.mkv" }, await SearchNamesAsync("t:arcane -t:jinx"));
        CollectionAssert.AreEqual(new[] { "Episode 2.mkv" }, await SearchNamesAsync("episode t:arcane"));
        // Zander inherits from LoL through Arcane.
        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "Episode 2.mkv", "Episode 10.mkv" }, await SearchNamesAsync("t:jinx, t:lol e:mkv"));
        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "Episode 10.mkv" }, await SearchNamesAsync("t:jinx, t:lol -t:arcane"));
        Assert.IsEmpty(await SearchNamesAsync("t:nobody"));
    }

    [TestMethod]
    public async Task UntaggedFindsFilesWithoutTags()
    {
        await ScanAsync("Media");
        await TagAsync(["Media/photo.jpg"], BuiltInTags.Favorite);

        CollectionAssert.AreEquivalent(new[] { "Episode 10.mkv", "Episode 2.mkv", "Holiday 2020.mp4" }, await SearchNamesAsync("special:untagged", "Media"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("-special:untagged", "Media"));
    }

    [TestMethod]
    public async Task FilesWithAHiddenTagOnlyShowUpWhenTheSearchAsksForIt()
    {
        await ScanAsync("Media");
        await TagAsync(["Media/Episode 2.mkv"], BuiltInTags.Archived);

        CollectionAssert.AreEqual(new[] { "Episode 10.mkv" }, await SearchNamesAsync("episode"));
        CollectionAssert.AreEqual(new[] { "Episode 2.mkv" }, await SearchNamesAsync("t:archived"));
        CollectionAssert.AreEqual(new[] { "Episode 2.mkv" }, await SearchNamesAsync("episode t:archive"));
        // Searching for the parent of a hidden tag is asking for it too.
        CollectionAssert.AreEqual(new[] { "Episode 2.mkv" }, await SearchNamesAsync("t:meta"));
    }

    [TestMethod]
    public async Task TaggingAFileThatIsNotIndexedIndexesOnlyThatFile()
    {
        var result = await TagAsync(["ChildDir/AFile1.txt"], BuiltInTags.Favorite);

        Assert.AreEqual(1, result.Added);
        await using var db = await CreateDbAsync();
        // Not the whole folder, which could hold a million files.
        Assert.AreEqual(_library.FullPath("ChildDir/AFile1.txt"), (await db.Files.SingleAsync(TestContext.CancellationToken)).FullName);
        Assert.AreEqual(_library.FullPath("ChildDir"), (await db.Directories.SingleAsync(TestContext.CancellationToken)).FullName);
        CollectionAssert.AreEqual(new[] { "AFile1.txt" }, await SearchNamesAsync("t:favorite"));

        // A later scan of the folder adds the other files to the same folder row.
        await ScanAsync("ChildDir");
        Assert.AreEqual(2, await db.Files.CountAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task AddingAndRemovingTagsIsIdempotent()
    {
        var first = await TagAsync(["RFile1.txt", "RFile2"], BuiltInTags.Favorite);
        var again = await TagAsync(["RFile1.txt"], BuiltInTags.Favorite);
        var (removed, _) = await Tags.ChangeFileTagsAsync(new FileTagChange(["RFile2", "RFile1.txt"], Remove: [BuiltInTags.Favorite]), TestContext.CancellationToken);

        Assert.AreEqual(2, first.Added);
        Assert.AreEqual(0, again.Added);
        Assert.AreEqual(2, removed!.Removed);
        Assert.IsTrue(removed.Files.All(f => f.Tags.Count == 0));
    }

    [TestMethod]
    public async Task TagsFilesFromSeveralFoldersAtOnce()
    {
        // The client's selection can span folders (search results); some files already have one of the tags.
        var beach = await CreateAsync(new TagInput("Beach"));
        await TagAsync(["Media/photo.jpg"], beach.Id);
        await ScanAsync("Media");
        string[] selection = ["Media/photo.jpg", "Media/Episode 2.mkv", "ChildDir/AFile1.txt", "RFile1.txt"];

        var added = await TagAsync(selection, beach.Id, BuiltInTags.Favorite);
        var (removed, _) = await Tags.ChangeFileTagsAsync(
            new FileTagChange(["Media/photo.jpg", "RFile1.txt"], Remove: [beach.Id]), TestContext.CancellationToken);

        Assert.AreEqual(7, added.Added);
        CollectionAssert.AreEqual(selection, added.Files.Select(f => f.Path).ToArray());
        Assert.IsTrue(added.Files.All(f => f.Tags.Count == 2));
        Assert.AreEqual(2, removed!.Removed);
        CollectionAssert.AreEquivalent(new[] { "Episode 2.mkv", "AFile1.txt" }, await SearchNamesAsync("t:beach"));
        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "Episode 2.mkv", "AFile1.txt", "RFile1.txt" }, await SearchNamesAsync("t:favorite"));
    }

    [TestMethod]
    public async Task TaggingAFolderIndexesItAndTagsOnlyItsOwnFilesUnlessRecursive()
    {
        // Nothing indexed yet: the folder is indexed first.
        var own = await ChangeScopeAsync(new ScopeTagChange("RepeatedDir", Add: [BuiltInTags.Favorite]));
        var tree = await ChangeScopeAsync(new ScopeTagChange("RepeatedDir/", Recursive: true, Add: [BuiltInTags.Favorite]));
        var summary = await Scope.GetTagsAsync(new TagScope("RepeatedDir", Recursive: true), TestContext.CancellationToken);
        var direct = await Scope.GetTagsAsync(new TagScope("RepeatedDir/ChildDir"), TestContext.CancellationToken);

        Assert.AreEqual((2, 2), (own.Files, own.Added));
        Assert.AreEqual(new TagScope("RepeatedDir", Recursive: true), tree.Scope);
        Assert.AreEqual((6, 4), (tree.Files, tree.Added));
        Assert.AreEqual(6, summary!.Files);
        Assert.AreEqual((BuiltInTags.Favorite, 6), (summary.Tags.Single().Tag.Id, summary.Tags.Single().FileCount));
        Assert.AreEqual(2, direct!.Files);
        CollectionAssert.AreEquivalent(new[] { "RDFile1.txt", "RDFile2.txt", "AFile1.txt", "AFile2", "RDFile1.txt", "RDFile2.txt" }, await SearchNamesAsync("t:favorite"));
        // Not the folders next to it.
        Assert.IsEmpty(await SearchNamesAsync("t:favorite", "ChildDir"));

        var removed = await ChangeScopeAsync(new ScopeTagChange("RepeatedDir/RepeatedDir", Remove: [BuiltInTags.Favorite]));
        Assert.AreEqual((2, 0, 2), (removed.Files, removed.Added, removed.Removed));
        Assert.AreEqual(4, (await Scope.GetTagsAsync(new TagScope("RepeatedDir", Recursive: true), TestContext.CancellationToken))!.Tags.Single().FileCount);
    }

    [TestMethod]
    public async Task TaggingSearchResultsReachesEveryMatchEvenWhenTheChangeAltersTheMatches()
    {
        var watched = await CreateAsync(new TagInput("Watched"));
        await ScanAsync("");
        await TagAsync(["Media/Episode 2.mkv"], watched.Id);
        await TagAsync(["Media/Episode 10.mkv"], BuiltInTags.Archived);

        // The search excludes the tag it adds; the archived episode is hidden from it, so it is left alone.
        var scope = new TagScope("Media", Query: "e:mkv e:mp4 -t:watched");
        var before = await Scope.GetTagsAsync(scope, TestContext.CancellationToken);
        var result = await ChangeScopeAsync(new ScopeTagChange("Media", Recursive: true, Query: " e:mkv e:mp4 -t:watched ", Add: [watched.Id]));

        Assert.AreEqual(1, before!.Files);
        Assert.IsEmpty(before.Tags);
        Assert.AreEqual(new TagScope("Media", Query: "e:mkv e:mp4 -t:watched"), result.Scope);
        Assert.AreEqual((1, 1), (result.Files, result.Added));
        CollectionAssert.AreEquivalent(new[] { "Holiday 2020.mp4", "Episode 2.mkv" }, await SearchNamesAsync("t:watched"));

        var all = await ChangeScopeAsync(new ScopeTagChange("", Query: "t:watched", Add: [BuiltInTags.Favorite], Remove: [watched.Id]));
        Assert.AreEqual((2, 2, 2), (all.Files, all.Added, all.Removed));
        CollectionAssert.AreEquivalent(new[] { "Holiday 2020.mp4", "Episode 2.mkv" }, await SearchNamesAsync("t:favorite"));
    }

    [TestMethod]
    public async Task TaggingAScopeRejectsMissingTagsAndFolders()
    {
        var nothing = await Scope.ChangeAsync(new ScopeTagChange("Media"), TestContext.CancellationToken);
        var unknown = await Scope.ChangeAsync(new ScopeTagChange("Media", Remove: [999]), TestContext.CancellationToken);
        var file = await Scope.ChangeAsync(new ScopeTagChange("Media/photo.jpg", Add: [BuiltInTags.Favorite]), TestContext.CancellationToken);
        var outside = await Scope.ChangeAsync(new ScopeTagChange("..", Query: "x", Add: [BuiltInTags.Favorite]), TestContext.CancellationToken);

        Assert.AreEqual(ScopeTagStatus.Invalid, nothing.Status);
        Assert.IsTrue(unknown.Errors!.ContainsKey("remove"));
        Assert.AreEqual(ScopeTagStatus.NotFound, file.Status);
        Assert.AreEqual(ScopeTagStatus.NotFound, outside.Status);
        Assert.IsNull(await Scope.GetTagsAsync(new TagScope("Missing folder"), TestContext.CancellationToken));
        await using var db = await CreateDbAsync();
        Assert.AreEqual(0, await db.Files.CountAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task RejectsUnknownTagsAndPathsOutsideTheLibrary()
    {
        var (_, unknownTag) = await Tags.ChangeFileTagsAsync(new FileTagChange(["RFile1.txt"], Add: [999]), TestContext.CancellationToken);
        var (_, outside) = await Tags.ChangeFileTagsAsync(new FileTagChange(["../index.db"], Add: [BuiltInTags.Favorite]), TestContext.CancellationToken);
        var (_, folder) = await Tags.ChangeFileTagsAsync(new FileTagChange(["Media"], Add: [BuiltInTags.Favorite]), TestContext.CancellationToken);

        Assert.IsTrue(unknownTag!.ContainsKey("add"));
        Assert.IsTrue(outside!.ContainsKey("paths"));
        Assert.IsTrue(folder!.ContainsKey("paths"));
    }

    [TestMethod]
    public async Task DisambiguationParentIsShownAfterTheName()
    {
        var fnaf = await CreateAsync(new TagInput("Five Nights at Freddy's", Shorthand: "FNAF"));
        var freddy = await CreateAsync(new TagInput("Freddy", ParentIds: [fnaf.Id], DisambiguationId: fnaf.Id));
        var orphan = await CreateAsync(new TagInput("Freddy", DisambiguationId: fnaf.Id));

        Assert.AreEqual("Freddy (FNAF)", freddy.DisplayName);
        // The disambiguation tag must be a parent: otherwise it is dropped, as in TagStudio.
        Assert.IsNull(orphan.DisambiguationId);
        Assert.AreEqual("Freddy", orphan.DisplayName);
    }

    [TestMethod]
    public async Task RefusesParentsThatWouldMakeALoop()
    {
        var a = await CreateAsync(new TagInput("A"));
        var b = await CreateAsync(new TagInput("B", ParentIds: [a.Id]));
        var c = await CreateAsync(new TagInput("C", ParentIds: [b.Id]));

        var loop = await Tags.UpdateAsync(a.Id, new TagInput("A", ParentIds: [c.Id]), TestContext.CancellationToken);
        var self = await Tags.UpdateAsync(a.Id, new TagInput("A", ParentIds: [a.Id]), TestContext.CancellationToken);

        Assert.AreEqual(TagEditStatus.Invalid, loop.Status);
        Assert.AreEqual(TagEditStatus.Invalid, self.Status);
    }

    [TestMethod]
    public async Task UpdateReplacesAliasesAndParents()
    {
        var animal = await CreateAsync(new TagInput("Animal"));
        var pet = await CreateAsync(new TagInput("Pet"));
        var cat = await CreateAsync(new TagInput("Cat", Aliases: ["Kitty", "Feline"], ParentIds: [animal.Id]));

        var updated = await Tags.UpdateAsync(cat.Id, new TagInput(" Cat ", Color: "orange", Aliases: ["Feline", " Kitten ", "kitten"], ParentIds: [pet.Id]), TestContext.CancellationToken);

        Assert.AreEqual(TagEditStatus.Ok, updated.Status);
        Assert.AreEqual("Cat", updated.Tag!.Name);
        Assert.AreEqual("orange", updated.Tag.Color);
        CollectionAssert.AreEqual(new[] { "Feline", "Kitten" }, updated.Tag.Aliases.ToArray());
        CollectionAssert.AreEqual(new[] { "Pet" }, updated.Tag.Parents.Select(p => p.Name).ToArray());
        Assert.AreEqual(TagEditStatus.Invalid, (await Tags.UpdateAsync(cat.Id, new TagInput("Cat", Color: "plaid"), TestContext.CancellationToken)).Status);
    }

    [TestMethod]
    public async Task DeletingATagUnlinksItEverywhere()
    {
        var parent = await CreateAsync(new TagInput("Parent", Shorthand: "P"));
        var tag = await CreateAsync(new TagInput("Doomed", Aliases: ["Gone"], ParentIds: [parent.Id]));
        var child = await CreateAsync(new TagInput("Child", ParentIds: [tag.Id], DisambiguationId: tag.Id));
        await TagAsync(["RFile1.txt"], tag.Id);

        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(tag.Id, TestContext.CancellationToken));

        Assert.IsNull(await Tags.GetAsync(tag.Id, TestContext.CancellationToken));
        var orphan = await Tags.GetAsync(child.Id, TestContext.CancellationToken);
        Assert.IsEmpty(orphan!.Parents);
        Assert.IsNull(orphan.DisambiguationId);
        Assert.IsEmpty((await Tags.GetFileTagsAsync("RFile1.txt", TestContext.CancellationToken))!.Tags);
        Assert.AreEqual(TagDeleteStatus.BuiltIn, await Tags.DeleteAsync(BuiltInTags.Favorite, TestContext.CancellationToken));
        Assert.AreEqual(TagDeleteStatus.NotFound, await Tags.DeleteAsync(tag.Id, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task TagSearchPrefersNamesStartingWithTheText()
    {
        var franchise = await CreateAsync(new TagInput("Movie Franchise"));
        await CreateAsync(new TagInput("Shrek (Movie Franchise)", ParentIds: [franchise.Id]));
        await CreateAsync(new TagInput("Shrek", ParentIds: [franchise.Id]));
        await CreateAsync(new TagInput("Big Shrek Energy"));
        await CreateAsync(new TagInput("Ogre", Aliases: ["Shrekkish"]));

        var results = await Tags.SearchAsync("shrek", 100, TestContext.CancellationToken);
        var franchiseResults = await Tags.SearchAsync("franchise", 100, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "Shrek", "Ogre", "Shrek (Movie Franchise)", "Big Shrek Energy" }, results.Matches.Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Movie Franchise", "Shrek (Movie Franchise)" }, franchiseResults.Matches.Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Shrek" }, franchiseResults.Descendants.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task FileTagsAreGroupedByCategory()
    {
        var character = await CreateAsync(new TagInput("Character", IsCategory: true));
        var shrek = await CreateAsync(new TagInput("Shrek", ParentIds: [character.Id]));
        var swamp = await CreateAsync(new TagInput("Swamp"));
        await TagAsync(["Media/photo.jpg"], shrek.Id, swamp.Id, BuiltInTags.Favorite, character.Id);

        var tags = (await Tags.GetFileTagsAsync("Media/photo.jpg", TestContext.CancellationToken))!;

        CollectionAssert.AreEqual(new[] { "Character", "Favorite", "Shrek", "Swamp" }, tags.Tags.Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Character", "Meta Tags", null }, tags.Groups.Select(g => g.Category?.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Character", "Shrek" }, tags.Groups[0].Tags.Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Favorite" }, tags.Groups[1].Tags.Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Swamp" }, tags.Groups[2].Tags.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task TagsSurviveARescanAndGoAwayWithTheirFile()
    {
        await ScanAsync("");
        await TagAsync(["Media/photo.jpg", "RFile1.txt"], BuiltInTags.Favorite);
        File.Delete(_library.FullPath("RFile1.txt"));

        await ScanAsync("");

        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:favorite"));
        await using var db = await CreateDbAsync();
        Assert.AreEqual(1, await db.FileTags.CountAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SearchResultsAndListingsCarryTags()
    {
        await TagAsync(["Media/photo.jpg"], BuiltInTags.Favorite);

        var found = (await _services.Get<FileSearchService>().SearchAsync("photo", "", null, 10, cancellationToken: TestContext.CancellationToken))!.Items.Single();
        var listing = _services.Get<Sfe.Core.Library.LibraryBrowser>().List("Media", 0, 10)!;
        var listed = await Tags.AttachIndexDataAsync(listing.Items, TestContext.CancellationToken);

        Assert.AreEqual("Favorite", found.Tags!.Single().Name);
        Assert.AreEqual("Favorite", listed.Single(i => i.Name == "photo.jpg").Tags!.Single().Name);
        Assert.IsEmpty(listed.Single(i => i.Name == "Episode 2.mkv").Tags!);
    }

    [TestMethod]
    public async Task ListingsTellWhichFilesAndFoldersAreNotIndexed()
    {
        await ScanAsync("Media");
        _library.AddFile("Media/after the scan.jpg", 10);
        var browser = _services.Get<Sfe.Core.Library.LibraryBrowser>();

        var root = await Tags.AttachIndexDataAsync(browser.List("", 0, 100)!.Items, TestContext.CancellationToken);
        var media = await Tags.AttachIndexDataAsync(browser.List("Media", 0, 100)!.Items, TestContext.CancellationToken);

        Assert.IsTrue(root.Single(i => i.Name == "Media").Indexed);
        Assert.IsFalse(root.Single(i => i.Name == "ChildDir").Indexed);
        Assert.IsFalse(root.Single(i => i.Name == "RFile1.txt").Indexed);
        Assert.IsTrue(media.Single(i => i.Name == "photo.jpg").Indexed);
        Assert.IsTrue(media.Single(i => i.Name == "Episode 2.mkv").Indexed);
        Assert.IsFalse(media.Single(i => i.Name == "after the scan.jpg").Indexed);
        Assert.IsEmpty(media.Single(i => i.Name == "after the scan.jpg").Tags!);
    }

    [TestMethod]
    public async Task AddsTheTaggingTablesToAnExistingSqliteIndex()
    {
        // The index as the version before tagging created it.
        await using (var db = await CreateDbAsync())
        {
            await db.Database.ExecuteSqlAsync($"DROP TABLE TagActivity", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"DROP TABLE FileTags", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"DROP TABLE TagParents", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"DROP TABLE TagAliases", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"DROP TABLE TagDefinitions", TestContext.CancellationToken);
        }

        await _services.Get<DatabaseInitializer>().InitializeAsync(TestContext.CancellationToken);

        Assert.HasCount(3, (await Tags.SearchAsync(null, 100, TestContext.CancellationToken)).Matches);
        Assert.AreEqual(1, (await TagAsync(["RFile1.txt"], BuiltInTags.Favorite)).Added);
        Assert.IsTrue(await Tags.RecordViewedAsync("RFile1.txt", TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SidebarListsPopularSearchedViewedAndRandomTags()
    {
        var created = new List<TagDetails>();
        for (var i = 1; i <= 12; i++)
            created.Add(await CreateAsync(new TagInput($"Tag {i:00}")));
        await ScanAsync("Media");
        // Usage: Tag 01 on 4 files, Tag 02 on 3, Tag 03 on 2, Tag 04 and Favorite on 1.
        await TagAsync(["Media/photo.jpg", "Media/Episode 2.mkv", "Media/Episode 10.mkv", "Media/Holiday 2020.mp4"], created[0].Id);
        await TagAsync(["Media/photo.jpg", "Media/Episode 2.mkv", "Media/Episode 10.mkv"], created[1].Id);
        await TagAsync(["Media/photo.jpg", "Media/Episode 2.mkv"], created[2].Id);
        await TagAsync(["Media/photo.jpg"], created[3].Id, BuiltInTags.Favorite);
        await TagAsync(["RFile1.txt"], created[4].Id, created[5].Id);

        // Searches record the tags they name, not their descendants nor the negated ones; next pages don't count.
        await SearchNamesAsync("t:\"tag 07\"");
        await SearchNamesAsync("t:tag_08 -t:\"tag 09\"");
        await _services.Get<FileSearchService>().SearchAsync("t:\"tag 10\"", "", "RFile1.txt", 10, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(await Tags.RecordViewedAsync("RFile1.txt", TestContext.CancellationToken));
        Assert.IsFalse(await Tags.RecordViewedAsync("Media", TestContext.CancellationToken));

        var sidebar = await Tags.GetSidebarAsync(TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "Tag 01", "Tag 02", "Tag 03", "Favorite", "Tag 04" }, sidebar.Popular.Select(u => u.Tag.Name).ToArray());
        CollectionAssert.AreEqual(new[] { 4, 3, 2, 1, 1 }, sidebar.Popular.Select(u => u.FileCount).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Tag 07", "Tag 08" }, sidebar.Searched.Select(u => u.Tag.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Tag 05", "Tag 06" }, sidebar.Viewed.Select(u => u.Tag.Name).ToArray());
        Assert.HasCount(5, sidebar.Lucky);
        var shown = sidebar.Popular.Concat(sidebar.Searched).Concat(sidebar.Viewed).Select(u => u.Tag.Id).ToHashSet();
        Assert.IsFalse(sidebar.Lucky.Any(u => shown.Contains(u.Tag.Id)));
        Assert.HasCount(5, sidebar.Lucky.Select(u => u.Tag.Id).Distinct().ToList());

        // The most recent search comes first; deleting a tag takes it out of the lists.
        await SearchNamesAsync("t:\"tag 11\"");
        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(created[4].Id, TestContext.CancellationToken));
        var after = await Tags.GetSidebarAsync(TestContext.CancellationToken);
        Assert.AreEqual("Tag 11", after.Searched[0].Tag.Name);
        CollectionAssert.AreEqual(new[] { "Tag 06" }, after.Viewed.Select(u => u.Tag.Name).ToArray());
    }

    /* ---------- Adding and removing tags ---------- */

    [TestMethod]
    public async Task AddsAndRemovesTagsInOneChange()
    {
        var old = await CreateAsync(new TagInput("Old"));
        var keep = await CreateAsync(new TagInput("Keep"));
        var fresh = await CreateAsync(new TagInput("New"));
        await TagAsync(["RFile1.txt", "RFile2"], old.Id, keep.Id);

        var result = await ChangeAsync(new FileTagChange(["RFile1.txt", "RFile2"], Add: [fresh.Id], Remove: [old.Id]));

        Assert.AreEqual(2, result.Added);
        Assert.AreEqual(2, result.Removed);
        Assert.IsTrue(result.Files.All(f => f.Tags.Select(t => t.Name).SequenceEqual(["Keep", "New"])));
        Assert.IsEmpty(await SearchNamesAsync("t:old"));
        CollectionAssert.AreEquivalent(new[] { "RFile1.txt", "RFile2" }, await SearchNamesAsync("t:new t:keep"));
    }

    [TestMethod]
    public async Task AddingAndRemovingTheSameTagAddsIt()
    {
        var result = await ChangeAsync(new FileTagChange(["RFile1.txt"], Add: [BuiltInTags.Favorite], Remove: [BuiltInTags.Favorite]));

        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(0, result.Removed);
        Assert.AreEqual("Favorite", result.Files.Single().Tags.Single().Name);
    }

    [TestMethod]
    public async Task RemovingATagOnlyTouchesTheFilesThatHaveIt()
    {
        var tag = await CreateAsync(new TagInput("Partial"));
        await TagAsync(["RFile1.txt"], tag.Id, BuiltInTags.Favorite);
        await TagAsync(["RFile2"], BuiltInTags.Favorite);

        var result = await ChangeAsync(new FileTagChange(["RFile1.txt", "RFile2", "100%_done[1].txt"], Remove: [tag.Id]));
        var again = await ChangeAsync(new FileTagChange(["RFile1.txt"], Remove: [tag.Id]));

        Assert.AreEqual(1, result.Removed);
        Assert.AreEqual(0, again.Removed);
        CollectionAssert.AreEqual(new[] { "Favorite" }, result.Files[0].Tags.Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Favorite" }, result.Files[1].Tags.Select(t => t.Name).ToArray());
        Assert.IsEmpty(result.Files[2].Tags);
        CollectionAssert.AreEquivalent(new[] { "RFile1.txt", "RFile2" }, await SearchNamesAsync("t:favorite"));
    }

    [TestMethod]
    public async Task RemovingTheLastTagMakesAFileUntaggedAgain()
    {
        await ScanAsync("ChildDir");
        await TagAsync(["ChildDir/AFile1.txt"], BuiltInTags.Favorite);
        CollectionAssert.AreEqual(new[] { "AFile2" }, await SearchNamesAsync("special:untagged", "ChildDir"));

        await ChangeAsync(new FileTagChange(["ChildDir/AFile1.txt"], Remove: [BuiltInTags.Favorite]));

        CollectionAssert.AreEquivalent(new[] { "AFile1.txt", "AFile2" }, await SearchNamesAsync("special:untagged", "ChildDir"));
        Assert.IsEmpty(await SearchNamesAsync("-special:untagged", "ChildDir"));
    }

    [TestMethod]
    public async Task TagsAThousandFilesAtOnce()
    {
        var paths = Enumerable.Range(0, TagService.MaxFilesPerChange).Select(i => $"Bulk/file{i:D4}.txt").ToArray();
        foreach (var path in paths)
            _library.AddFile(path, 1);
        var bulk = await CreateAsync(new TagInput("Bulk"));

        var added = await TagAsync(paths, bulk.Id, BuiltInTags.Favorite);
        var again = await TagAsync(paths, bulk.Id, BuiltInTags.Favorite);

        Assert.AreEqual(2 * paths.Length, added.Added);
        Assert.AreEqual(0, again.Added);
        Assert.HasCount(paths.Length, added.Files);
        CollectionAssert.AreEqual(paths, added.Files.Select(f => f.Path).ToArray());
        Assert.AreEqual(paths.Length, (await Tags.GetAsync(bulk.Id, TestContext.CancellationToken))!.FileCount);
        Assert.AreEqual(paths.Length, await CountFoundAsync("t:bulk t:favorite", "Bulk"));

        // Massive removal from every other file.
        var half = paths.Where((_, i) => i % 2 == 0).ToArray();
        var removed = await ChangeAsync(new FileTagChange(half, Remove: [bulk.Id]));

        Assert.AreEqual(half.Length, removed.Removed);
        Assert.AreEqual(paths.Length - half.Length, (await Tags.GetAsync(bulk.Id, TestContext.CancellationToken))!.FileCount);
        Assert.AreEqual(paths.Length - half.Length, await CountFoundAsync("t:bulk", "Bulk"));
        Assert.AreEqual(half.Length, await CountFoundAsync("t:favorite -t:bulk", "Bulk"));
        await using var db = await CreateDbAsync();
        Assert.AreEqual(paths.Length + paths.Length - half.Length, await db.FileTags.CountAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task RefusesMoreFilesThanTheLimitAndChangesNothing()
    {
        var paths = Enumerable.Range(0, TagService.MaxFilesPerChange + 1).Select(i => i % 2 == 0 ? "RFile1.txt" : "RFile2").ToArray();

        var (result, errors) = await Tags.ChangeFileTagsAsync(new FileTagChange(paths, Add: [BuiltInTags.Favorite]), TestContext.CancellationToken);

        Assert.IsNull(result);
        Assert.IsTrue(errors!.ContainsKey("paths"));
        await using var db = await CreateDbAsync();
        Assert.AreEqual(0, await db.FileTags.CountAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ValidatesTheChangeBeforeTaggingAnything()
    {
        var (_, empty) = await Tags.ChangeFileTagsAsync(new FileTagChange([], Add: [BuiltInTags.Favorite]), TestContext.CancellationToken);
        var (_, missing) = await Tags.ChangeFileTagsAsync(new FileTagChange(["RFile1.txt", "Nope.txt"], Add: [BuiltInTags.Favorite]), TestContext.CancellationToken);
        var (_, unknownRemoval) = await Tags.ChangeFileTagsAsync(new FileTagChange(["RFile1.txt"], Add: [BuiltInTags.Favorite], Remove: [999]), TestContext.CancellationToken);

        Assert.IsTrue(empty!.ContainsKey("paths"));
        Assert.IsTrue(missing!.ContainsKey("paths"));
        Assert.IsTrue(unknownRemoval!.ContainsKey("remove"));
        // The valid file of the rejected changes wasn't tagged.
        Assert.IsEmpty((await Tags.GetFileTagsAsync("RFile1.txt", TestContext.CancellationToken))!.Tags);
    }

    [TestMethod]
    public async Task RepeatedPathsAndTagsCountOnce()
    {
        var result = await ChangeAsync(new FileTagChange(["RFile1.txt", "RFile1.txt"], Add: [BuiltInTags.Favorite, BuiltInTags.Favorite]));

        Assert.AreEqual(1, result.Added);
        Assert.AreEqual("RFile1.txt", result.Files.Single().Path);
        Assert.AreEqual(1, (await Tags.GetAsync(BuiltInTags.Favorite, TestContext.CancellationToken))!.FileCount);
    }

    [TestMethod]
    public async Task FileCountsFollowEveryChange()
    {
        var tag = await CreateAsync(new TagInput("Counted"));
        Assert.AreEqual(0, tag.FileCount);

        await TagAsync(["RFile1.txt", "RFile2", "Media/photo.jpg"], tag.Id);
        Assert.AreEqual(3, (await Tags.GetAsync(tag.Id, TestContext.CancellationToken))!.FileCount);

        await ChangeAsync(new FileTagChange(["RFile2"], Remove: [tag.Id]));
        Assert.AreEqual(2, (await Tags.GetAsync(tag.Id, TestContext.CancellationToken))!.FileCount);
        Assert.AreEqual(2, (await Tags.SearchAsync("counted", 10, TestContext.CancellationToken)).Matches.Single().FileCount);
    }

    /* ---------- Deleting tags that files use ---------- */

    [TestMethod]
    public async Task DeletingATagUsedByManyFilesKeepsTheFilesAndTheirOtherTags()
    {
        await ScanAsync("Media");
        var doomed = await CreateAsync(new TagInput("Doomed"));
        var keep = await CreateAsync(new TagInput("Keep"));
        string[] media = ["Media/photo.jpg", "Media/Episode 2.mkv", "Media/Episode 10.mkv", "Media/Holiday 2020.mp4"];
        await TagAsync(media, doomed.Id, keep.Id);
        await TagAsync(["Media/photo.jpg"], BuiltInTags.Favorite);
        await using var db = await CreateDbAsync();
        var files = await db.Files.CountAsync(TestContext.CancellationToken);

        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(doomed.Id, TestContext.CancellationToken));

        Assert.AreEqual(files, await db.Files.CountAsync(TestContext.CancellationToken));
        Assert.IsEmpty(await SearchNamesAsync("t:doomed"));
        Assert.HasCount(4, await SearchNamesAsync("t:keep"));
        Assert.AreEqual(4, (await Tags.GetAsync(keep.Id, TestContext.CancellationToken))!.FileCount);
        CollectionAssert.AreEqual(new[] { "Favorite", "Keep" },
            (await Tags.GetFileTagsAsync("Media/photo.jpg", TestContext.CancellationToken))!.Tags.Select(t => t.Name).ToArray());
        Assert.AreEqual(5, await db.FileTags.CountAsync(TestContext.CancellationToken));
        Assert.IsEmpty(await SearchNamesAsync("special:untagged", "Media"));
    }

    [TestMethod]
    public async Task DeletingAParentKeepsItsChildrenAndTheirFiles()
    {
        var animal = await CreateAsync(new TagInput("Animal"));
        var pet = await CreateAsync(new TagInput("Pet"));
        var cat = await CreateAsync(new TagInput("Cat", ParentIds: [animal.Id, pet.Id]));
        await TagAsync(["Media/photo.jpg"], cat.Id);
        await TagAsync(["RFile1.txt"], animal.Id);

        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(animal.Id, TestContext.CancellationToken));

        // The child keeps its other parent and its files; the files tagged with the parent itself lose that tag.
        CollectionAssert.AreEqual(new[] { "Pet" }, (await Tags.GetAsync(cat.Id, TestContext.CancellationToken))!.Parents.Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:cat"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:pet"));
        Assert.IsEmpty(await SearchNamesAsync("t:animal"));
        Assert.IsEmpty((await Tags.GetFileTagsAsync("RFile1.txt", TestContext.CancellationToken))!.Tags);
    }

    [TestMethod]
    public async Task DeletingAChildKeepsItsParentAndTheParentsFiles()
    {
        var animal = await CreateAsync(new TagInput("Animal"));
        var cat = await CreateAsync(new TagInput("Cat", ParentIds: [animal.Id]));
        await TagAsync(["Media/photo.jpg"], cat.Id);
        await TagAsync(["RFile1.txt"], animal.Id);

        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(cat.Id, TestContext.CancellationToken));

        CollectionAssert.AreEqual(new[] { "RFile1.txt" }, await SearchNamesAsync("t:animal"));
        Assert.IsEmpty((await Tags.SearchAsync("animal", 10, TestContext.CancellationToken)).Descendants);
    }

    [TestMethod]
    public async Task BuiltInTagsCanBeEditedButNotDeleted()
    {
        await TagAsync(["RFile1.txt"], BuiltInTags.Favorite);

        var updated = await Tags.UpdateAsync(BuiltInTags.Favorite, new TagInput("Starred", Color: "yellow", ParentIds: [BuiltInTags.Meta]), TestContext.CancellationToken);

        Assert.AreEqual(TagEditStatus.Ok, updated.Status);
        Assert.IsTrue(updated.Tag!.IsBuiltIn);
        Assert.AreEqual(TagDeleteStatus.BuiltIn, await Tags.DeleteAsync(BuiltInTags.Favorite, TestContext.CancellationToken));
        Assert.AreEqual(TagDeleteStatus.BuiltIn, await Tags.DeleteAsync(BuiltInTags.Meta, TestContext.CancellationToken));
        CollectionAssert.AreEqual(new[] { "RFile1.txt" }, await SearchNamesAsync("t:starred"));
        CollectionAssert.AreEqual(new[] { "RFile1.txt" }, await SearchNamesAsync("t:\"meta tags\""));
    }

    /* ---------- Parents ---------- */

    [TestMethod]
    public async Task EveryAncestorFindsTheFilesOfADeepHierarchy()
    {
        var ids = new List<int>();
        for (var level = 1; level <= 5; level++)
            ids.Add((await CreateAsync(new TagInput($"Level {level}", ParentIds: ids.Count > 0 ? [ids[^1]] : null))).Id);
        await TagAsync(["RFile1.txt"], ids[^1]);
        await TagAsync(["RFile2"], ids[2]);

        for (var level = 1; level <= 3; level++)
            CollectionAssert.AreEquivalent(new[] { "RFile1.txt", "RFile2" }, await SearchNamesAsync($"t:level_{level}"), $"Level {level}");
        CollectionAssert.AreEqual(new[] { "RFile1.txt" }, await SearchNamesAsync("t:\"level 4\""));
        CollectionAssert.AreEqual(new[] { "RFile1.txt" }, await SearchNamesAsync("t:\"level 5\""));
        CollectionAssert.AreEqual(new[] { "RFile2" }, await SearchNamesAsync("t:\"level 1\" -t:\"level 4\""));
        Assert.HasCount(4, (await Tags.SearchAsync("level 1", 100, TestContext.CancellationToken)).Descendants);
    }

    [TestMethod]
    public async Task AFileIsFoundOnceThroughSeveralParents()
    {
        // A diamond: Child inherits from Left and Right, which both inherit from Top.
        var top = await CreateAsync(new TagInput("Top"));
        var left = await CreateAsync(new TagInput("Left", ParentIds: [top.Id]));
        var right = await CreateAsync(new TagInput("Right", ParentIds: [top.Id]));
        var child = await CreateAsync(new TagInput("Child", ParentIds: [left.Id, right.Id]));
        await TagAsync(["Media/photo.jpg"], child.Id, left.Id);

        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:top"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:left"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:right"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:left t:right, t:top"));
        CollectionAssert.AreEquivalent(new[] { "Left", "Right" }, child.Parents.Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public async Task ChangingParentsChangesWhatSearchesFind()
    {
        var animal = await CreateAsync(new TagInput("Animal"));
        var plant = await CreateAsync(new TagInput("Plant"));
        var cactus = await CreateAsync(new TagInput("Cactus"));
        await TagAsync(["Media/photo.jpg"], cactus.Id);
        Assert.IsEmpty(await SearchNamesAsync("t:plant"));

        // Adding a parent later makes the files tagged before findable through it.
        await UpdateAsync(cactus.Id, new TagInput("Cactus", ParentIds: [plant.Id]));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:plant"));

        // Moving it under another parent.
        await UpdateAsync(cactus.Id, new TagInput("Cactus", ParentIds: [animal.Id]));
        Assert.IsEmpty(await SearchNamesAsync("t:plant"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:animal"));

        // Removing every parent: only its own name finds it.
        await UpdateAsync(cactus.Id, new TagInput("Cactus"));
        Assert.IsEmpty(await SearchNamesAsync("t:animal"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:cactus"));
        // Parent edits never touch the file links.
        CollectionAssert.AreEqual(new[] { "Cactus" }, (await Tags.GetFileTagsAsync("Media/photo.jpg", TestContext.CancellationToken))!.Tags.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task ParentsAreValidated()
    {
        var a = await CreateAsync(new TagInput("A"));
        var b = await CreateAsync(new TagInput("B", ParentIds: [a.Id]));
        var c = await CreateAsync(new TagInput("C", ParentIds: [a.Id]));
        var d = await CreateAsync(new TagInput("D", ParentIds: [b.Id, c.Id]));

        // A loop through either branch of the diamond, or a parent that doesn't exist.
        var loop = await Tags.UpdateAsync(a.Id, new TagInput("A", ParentIds: [d.Id]), TestContext.CancellationToken);
        var unknown = await Tags.CreateAsync(new TagInput("E", ParentIds: [a.Id, 999]), TestContext.CancellationToken);
        // Siblings and a repeated parent are fine.
        var sibling = await Tags.UpdateAsync(b.Id, new TagInput("B", ParentIds: [a.Id, c.Id, c.Id]), TestContext.CancellationToken);

        Assert.AreEqual(TagEditStatus.Invalid, loop.Status);
        Assert.IsTrue(loop.Errors!.ContainsKey("parentIds"));
        Assert.AreEqual(TagEditStatus.Invalid, unknown.Status);
        Assert.IsTrue(unknown.Errors!.ContainsKey("parentIds"));
        Assert.AreEqual(TagEditStatus.Ok, sibling.Status);
        CollectionAssert.AreEquivalent(new[] { "A", "C" }, sibling.Tag!.Parents.Select(p => p.Name).ToArray());
        Assert.HasCount(4, (await Tags.SearchAsync(null, 100, TestContext.CancellationToken)).Matches.Where(t => !t.IsBuiltIn).ToList());
    }

    [TestMethod]
    public async Task DisambiguationIsDroppedWithItsParent()
    {
        var fnaf = await CreateAsync(new TagInput("Five Nights at Freddy's", Shorthand: "FNAF"));
        var mercury = await CreateAsync(new TagInput("Queen"));
        var freddy = await CreateAsync(new TagInput("Freddy", ParentIds: [fnaf.Id, mercury.Id], DisambiguationId: fnaf.Id));
        Assert.AreEqual("Freddy (FNAF)", freddy.DisplayName);

        var switched = await UpdateAsync(freddy.Id, new TagInput("Freddy", ParentIds: [fnaf.Id, mercury.Id], DisambiguationId: mercury.Id));
        var dropped = await UpdateAsync(freddy.Id, new TagInput("Freddy", ParentIds: [fnaf.Id], DisambiguationId: mercury.Id));

        Assert.AreEqual("Freddy (Queen)", switched.DisplayName);
        Assert.IsNull(dropped.DisambiguationId);
        Assert.AreEqual("Freddy", dropped.DisplayName);
    }

    [TestMethod]
    public async Task ATagIsGroupedUnderEveryCategoryItInherits()
    {
        var people = await CreateAsync(new TagInput("People", IsCategory: true));
        var music = await CreateAsync(new TagInput("Music", IsCategory: true));
        var singer = await CreateAsync(new TagInput("Singer", ParentIds: [people.Id]));
        var freddie = await CreateAsync(new TagInput("Freddie Mercury", ParentIds: [singer.Id, music.Id]));
        await TagAsync(["Media/photo.jpg"], freddie.Id);

        var groups = (await Tags.GetFileTagsAsync("Media/photo.jpg", TestContext.CancellationToken))!.Groups;

        CollectionAssert.AreEqual(new[] { "Music", "People" }, groups.Select(g => g.Category!.Name).ToArray());
        Assert.IsTrue(groups.All(g => g.Tags.Single().Name == "Freddie Mercury"));
    }

    /* ---------- Tag validation ---------- */

    [TestMethod]
    [DataRow("", null, null, "name")]
    [DataRow("   ", null, null, "name")]
    [DataRow("Ok", null, "plaid", "color")]
    [DataRow("Ok", "S", null, null)]
    public async Task ValidatesTagValues(string name, string? shorthand, string? color, string? invalidField)
    {
        var result = await Tags.CreateAsync(new TagInput(name, Shorthand: shorthand, Color: color), TestContext.CancellationToken);

        if (invalidField is null)
        {
            Assert.AreEqual(TagEditStatus.Ok, result.Status);
        }
        else
        {
            Assert.AreEqual(TagEditStatus.Invalid, result.Status);
            Assert.IsTrue(result.Errors!.ContainsKey(invalidField));
        }
    }

    [TestMethod]
    public async Task LimitsTheLengthOfNames()
    {
        var tooLong = new string('x', SfeDbContext.MaxTagNameLength + 1);
        var longest = new string('x', SfeDbContext.MaxTagNameLength);

        Assert.IsTrue((await Tags.CreateAsync(new TagInput(tooLong), TestContext.CancellationToken)).Errors!.ContainsKey("name"));
        Assert.IsTrue((await Tags.CreateAsync(new TagInput("A", Shorthand: tooLong), TestContext.CancellationToken)).Errors!.ContainsKey("shorthand"));
        Assert.IsTrue((await Tags.CreateAsync(new TagInput("A", Aliases: ["ok", tooLong]), TestContext.CancellationToken)).Errors!.ContainsKey("aliases"));
        Assert.AreEqual(TagEditStatus.Ok, (await Tags.CreateAsync(new TagInput(longest, Shorthand: longest, Aliases: [longest]), TestContext.CancellationToken)).Status);
    }

    [TestMethod]
    public async Task EditingOrDeletingAMissingTagReportsIt()
    {
        Assert.AreEqual(TagEditStatus.NotFound, (await Tags.UpdateAsync(999, new TagInput("Ghost"), TestContext.CancellationToken)).Status);
        Assert.AreEqual(TagDeleteStatus.NotFound, await Tags.DeleteAsync(999, TestContext.CancellationToken));
        Assert.IsNull(await Tags.GetAsync(999, TestContext.CancellationToken));
    }

    /* ---------- Tag manager edge cases ---------- */

    [TestMethod]
    public async Task TagsWithQuotesInTheirNamesAreFoundAsTheClientSearchesForThem()
    {
        // A search term cannot hold a double quote: the client drops them (tags.ts tagQuery), the server ignores them.
        var vinyl = await CreateAsync(new TagInput("12\" Vinyl", Aliases: ["\"Twelve\""]));
        await TagAsync(["Media/photo.jpg"], vinyl.Id);

        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:\"12 Vinyl\""));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:12_Vinyl"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:twelve"));
        Assert.IsEmpty(await SearchNamesAsync("t:12"));
        Assert.AreEqual(vinyl.Id, (await Tags.SearchAsync("12\"", 10, TestContext.CancellationToken)).Matches.Single().Id);
    }

    [TestMethod]
    public async Task TagsNamedLikeSearchSyntaxAreFoundByName()
    {
        string[] names = ["e:mp4", "-Minus", "Rock, Paper", "snake_case", "special:untagged", "*star", "s:>1mb"];
        var file = 0;
        string[] files = ["RFile1.txt", "RFile2", "Media/photo.jpg", "Media/Episode 2.mkv", "Media/Episode 10.mkv", "Media/Holiday 2020.mp4", "100%_done[1].txt"];
        foreach (var name in names)
            await TagAsync([files[file++]], (await CreateAsync(new TagInput(name))).Id);

        CollectionAssert.AreEqual(new[] { "RFile1.txt" }, await SearchNamesAsync("t:e:mp4"));
        CollectionAssert.AreEqual(new[] { "RFile2" }, await SearchNamesAsync("t:-Minus"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:\"Rock, Paper\""));
        CollectionAssert.AreEqual(new[] { "Episode 2.mkv" }, await SearchNamesAsync("t:snake_case"));
        CollectionAssert.AreEqual(new[] { "Episode 10.mkv" }, await SearchNamesAsync("t:special:untagged"));
        CollectionAssert.AreEqual(new[] { "Holiday 2020.mp4" }, await SearchNamesAsync("t:*star"));
        CollectionAssert.AreEqual(new[] { "100%_done[1].txt" }, await SearchNamesAsync("t:\"s:>1mb\""));
    }

    [TestMethod]
    public async Task TagSearchFindsTextAnywhereInNamesShorthandsAndAliasesWhateverTheCase()
    {
        var spanish = await CreateAsync(new TagInput("Ñandú Español", Shorthand: "ÑE"));
        var russian = await CreateAsync(new TagInput("Русский", Aliases: ["Russian language"]));
        var zalgo = await CreateAsync(new TagInput(TestLibrary.SpecialFolder));

        Assert.AreEqual(spanish.Id, (await Tags.SearchAsync("ñandú", 10, TestContext.CancellationToken)).Matches.Single().Id);
        Assert.AreEqual(spanish.Id, (await Tags.SearchAsync("ESPAÑOL", 10, TestContext.CancellationToken)).Matches.Single().Id);
        Assert.AreEqual(spanish.Id, (await Tags.SearchAsync("ñe", 10, TestContext.CancellationToken)).Matches.Single().Id);
        Assert.AreEqual(russian.Id, (await Tags.SearchAsync("русс", 10, TestContext.CancellationToken)).Matches.Single().Id);
        Assert.AreEqual(russian.Id, (await Tags.SearchAsync("LANGUAGE", 10, TestContext.CancellationToken)).Matches.Single().Id);
        Assert.AreEqual(zalgo.Id, (await Tags.SearchAsync(TestLibrary.SpecialFolder, 10, TestContext.CancellationToken)).Matches.Single().Id);
        Assert.IsEmpty((await Tags.SearchAsync("klingon", 10, TestContext.CancellationToken)).Matches);
    }

    [TestMethod]
    public async Task BlankTagSearchListsEveryTagByNameUpToTheLimit()
    {
        await CreateAsync(new TagInput("banana"));
        await CreateAsync(new TagInput("Apple"));
        await CreateAsync(new TagInput("cherry", ParentIds: [BuiltInTags.Meta]));

        var all = await Tags.SearchAsync("   ", 100, TestContext.CancellationToken);
        var two = await Tags.SearchAsync(null, 2, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "Apple", "Archived", "banana", "cherry", "Favorite", "Meta Tags" }, all.Matches.Select(t => t.Name).ToArray());
        Assert.IsEmpty(all.Descendants);
        CollectionAssert.AreEqual(new[] { "Apple", "Archived" }, two.Matches.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task TagSearchLimitCountsMatchesAndDescendantsTogether()
    {
        var fruit = await CreateAsync(new TagInput("Fruit"));
        var fruitcake = await CreateAsync(new TagInput("Fruitcake"));
        await CreateAsync(new TagInput("Apple", ParentIds: [fruit.Id]));
        await CreateAsync(new TagInput("Pear", ParentIds: [fruit.Id]));
        await CreateAsync(new TagInput("Slice", ParentIds: [fruitcake.Id]));

        var one = await Tags.SearchAsync("fruit", 1, TestContext.CancellationToken);
        var three = await Tags.SearchAsync("fruit", 3, TestContext.CancellationToken);
        var all = await Tags.SearchAsync("fruit", 100, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "Fruit" }, one.Matches.Select(t => t.Name).ToArray());
        Assert.IsEmpty(one.Descendants);
        Assert.HasCount(2, three.Matches);
        Assert.HasCount(1, three.Descendants);
        CollectionAssert.AreEquivalent(new[] { "Apple", "Pear", "Slice" }, all.Descendants.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task ATagIsListedOnceWhetherItMatchesOrInheritsFromSeveralMatches()
    {
        // "Cat food" matches by itself and inherits from "Cat"; "Kitten" inherits from both matches.
        var cat = await CreateAsync(new TagInput("Cat"));
        var catFood = await CreateAsync(new TagInput("Cat food", ParentIds: [cat.Id]));
        await CreateAsync(new TagInput("Kitten", ParentIds: [cat.Id, catFood.Id]));

        var results = await Tags.SearchAsync("cat", 100, TestContext.CancellationToken);

        CollectionAssert.AreEqual(new[] { "Cat", "Cat food" }, results.Matches.Select(t => t.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Kitten" }, results.Descendants.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task TagsMayShareANameAndADisambiguationTellsThemApart()
    {
        var fnaf = await CreateAsync(new TagInput("Five Nights at Freddy's", Shorthand: "FNAF"));
        var queen = await CreateAsync(new TagInput("Queen"));
        var bear = await CreateAsync(new TagInput("Freddy", ParentIds: [fnaf.Id], DisambiguationId: fnaf.Id));
        var singer = await CreateAsync(new TagInput("Freddy", ParentIds: [queen.Id], DisambiguationId: queen.Id));
        await TagAsync(["Media/photo.jpg"], bear.Id);
        await TagAsync(["RFile1.txt"], singer.Id);

        var found = await Tags.SearchAsync("freddy", 10, TestContext.CancellationToken);

        CollectionAssert.AreEquivalent(new[] { "Freddy (FNAF)", "Freddy (Queen)" }, found.Matches.Where(t => t.Name == "Freddy").Select(t => t.DisplayName).ToArray());
        // The name finds both; their parents tell them apart.
        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "RFile1.txt" }, await SearchNamesAsync("t:freddy"));
        CollectionAssert.AreEqual(new[] { "photo.jpg" }, await SearchNamesAsync("t:freddy t:fnaf"));
        CollectionAssert.AreEqual(new[] { "Freddy (Queen)" }, (await Tags.GetFileTagsAsync("RFile1.txt", TestContext.CancellationToken))!.Tags.Select(t => t.DisplayName).ToArray());
    }

    [TestMethod]
    public async Task RenamingATagChangesWhichSearchesFindItsFiles()
    {
        var tag = await CreateAsync(new TagInput("Holidy", Aliases: ["Vacation"]));
        await TagAsync(["Media/Holiday 2020.mp4"], tag.Id);

        await UpdateAsync(tag.Id, new TagInput("Holiday", Aliases: ["Vacation"]));

        Assert.IsEmpty(await SearchNamesAsync("t:holidy"));
        CollectionAssert.AreEqual(new[] { "Holiday 2020.mp4" }, await SearchNamesAsync("t:holiday"));
        CollectionAssert.AreEqual(new[] { "Holiday 2020.mp4" }, await SearchNamesAsync("t:vacation"));
        Assert.AreEqual("Holiday", (await Tags.GetFileTagsAsync("Media/Holiday 2020.mp4", TestContext.CancellationToken))!.Tags.Single().Name);
    }

    [TestMethod]
    public async Task HidingATagLaterHidesItsFilesUntilItIsShownAgain()
    {
        await ScanAsync("Media");
        var secret = await CreateAsync(new TagInput("Secret"));
        await TagAsync(["Media/Episode 2.mkv"], secret.Id);
        CollectionAssert.AreEquivalent(new[] { "Episode 2.mkv", "Episode 10.mkv" }, await SearchNamesAsync("episode"));

        await UpdateAsync(secret.Id, new TagInput("Secret", IsHidden: true));
        CollectionAssert.AreEqual(new[] { "Episode 10.mkv" }, await SearchNamesAsync("episode"));
        CollectionAssert.AreEqual(new[] { "Episode 2.mkv" }, await SearchNamesAsync("episode t:secret"));
        // Excluding it is not asking for it.
        CollectionAssert.AreEqual(new[] { "Episode 10.mkv" }, await SearchNamesAsync("episode -t:secret"));

        await UpdateAsync(secret.Id, new TagInput("Secret"));
        CollectionAssert.AreEquivalent(new[] { "Episode 2.mkv", "Episode 10.mkv" }, await SearchNamesAsync("episode"));
    }

    [TestMethod]
    public async Task MakingATagACategoryRegroupsTheFilesTags()
    {
        var people = await CreateAsync(new TagInput("People"));
        var alice = await CreateAsync(new TagInput("Alice", ParentIds: [people.Id]));
        await TagAsync(["Media/photo.jpg"], alice.Id);
        Assert.IsNull((await Tags.GetFileTagsAsync("Media/photo.jpg", TestContext.CancellationToken))!.Groups.Single().Category);

        await UpdateAsync(people.Id, new TagInput("People", IsCategory: true));
        var grouped = (await Tags.GetFileTagsAsync("Media/photo.jpg", TestContext.CancellationToken))!.Groups;
        Assert.AreEqual("People", grouped.Single().Category!.Name);

        // Deleting the category puts its tags back in the default group.
        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(people.Id, TestContext.CancellationToken));
        var regrouped = (await Tags.GetFileTagsAsync("Media/photo.jpg", TestContext.CancellationToken))!.Groups;
        Assert.IsNull(regrouped.Single().Category);
        Assert.AreEqual("Alice", regrouped.Single().Tags.Single().Name);
    }

    [TestMethod]
    public async Task BlankOptionalValuesAreStoredAsNone()
    {
        var created = await CreateAsync(new TagInput($"  {new string('x', SfeDbContext.MaxTagNameLength)}  ", Shorthand: "   ", Color: "  ", Aliases: ["", "  ", null!, " Real "]));

        Assert.AreEqual(SfeDbContext.MaxTagNameLength, created.Name.Length);
        Assert.IsNull(created.Shorthand);
        Assert.IsNull(created.Color);
        CollectionAssert.AreEqual(new[] { "Real" }, created.Aliases.ToArray());
    }

    [TestMethod]
    public async Task ChangingTheCaseOfAnAliasKeepsTheNewCase()
    {
        var tag = await CreateAsync(new TagInput("Cat", Aliases: ["kitty"]));

        var updated = await UpdateAsync(tag.Id, new TagInput("Cat", Aliases: ["Kitty"]));

        CollectionAssert.AreEqual(new[] { "Kitty" }, updated.Aliases.ToArray());
        await using var db = await CreateDbAsync();
        Assert.AreEqual(1, await db.TagAliases.CountAsync(a => a.TagId == tag.Id, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ATagCannotBeItsOwnDisambiguation()
    {
        var parent = await CreateAsync(new TagInput("Parent"));
        var tag = await CreateAsync(new TagInput("Tag", ParentIds: [parent.Id]));

        var updated = await UpdateAsync(tag.Id, new TagInput("Tag", ParentIds: [parent.Id], DisambiguationId: tag.Id));

        Assert.IsNull(updated.DisambiguationId);
        Assert.AreEqual("Tag", updated.DisplayName);
    }

    [TestMethod]
    public async Task ARefusedEditChangesNothing()
    {
        var a = await CreateAsync(new TagInput("A"));
        var b = await CreateAsync(new TagInput("B", Aliases: ["Bee"], ParentIds: [a.Id]));

        var loop = await Tags.UpdateAsync(a.Id, new TagInput("A renamed", Aliases: ["new"], ParentIds: [b.Id]), TestContext.CancellationToken);

        Assert.AreEqual(TagEditStatus.Invalid, loop.Status);
        var unchanged = (await Tags.GetAsync(a.Id, TestContext.CancellationToken))!;
        Assert.AreEqual("A", unchanged.Name);
        Assert.IsEmpty(unchanged.Aliases);
        Assert.IsEmpty(unchanged.Parents);
    }

    [TestMethod]
    public async Task ATagCreatedAgainAfterADeleteStartsWithoutFiles()
    {
        var first = await CreateAsync(new TagInput("Phoenix"));
        await TagAsync(["RFile1.txt"], first.Id);
        Assert.AreEqual(TagDeleteStatus.Deleted, await Tags.DeleteAsync(first.Id, TestContext.CancellationToken));

        var again = await CreateAsync(new TagInput("Phoenix"));

        Assert.AreNotEqual(first.Id, again.Id);
        Assert.AreEqual(0, again.FileCount);
        Assert.IsEmpty(await SearchNamesAsync("t:phoenix"));
    }

    [TestMethod]
    public async Task TagsFilesWithUnicodeAndSpecialCharactersInTheirPaths()
    {
        string[] odd = [$"SpecialDir/{TestLibrary.SpecialFolder}/SFРусский.txt", "SpecialDir/SDEspañol", "100%_done[1].txt"];

        var result = await TagAsync(odd, BuiltInTags.Favorite);

        CollectionAssert.AreEqual(odd, result.Files.Select(f => f.Path).ToArray());
        CollectionAssert.AreEquivalent(new[] { "SFРусский.txt", "SDEspañol", "100%_done[1].txt" }, await SearchNamesAsync("t:favorite"));
        Assert.AreEqual("Favorite", (await Tags.GetFileTagsAsync(odd[0], TestContext.CancellationToken))!.Tags.Single().Name);
    }

    [TestMethod]
    public async Task FileTagsOfAMissingOrOutsidePathAreNotFound()
    {
        Assert.IsNull(await Tags.GetFileTagsAsync("Nope.txt", TestContext.CancellationToken));
        Assert.IsNull(await Tags.GetFileTagsAsync("../index.db", TestContext.CancellationToken));
        Assert.IsNull(await Tags.GetFileTagsAsync("Media", TestContext.CancellationToken));
        Assert.IsNull(await Tags.GetFileTagsAsync(null, TestContext.CancellationToken));
        // A file that exists but was never indexed has no tags yet.
        Assert.IsEmpty((await Tags.GetFileTagsAsync("RFile2", TestContext.CancellationToken))!.Tags);
    }

    [TestMethod]
    public async Task AChangeWithNothingToAddOrRemoveOnlyReportsTheTags()
    {
        await TagAsync(["RFile1.txt"], BuiltInTags.Favorite);

        var result = await ChangeAsync(new FileTagChange(["RFile1.txt", "RFile2"]));

        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(0, result.Removed);
        Assert.AreEqual("Favorite", result.Files[0].Tags.Single().Name);
        Assert.IsEmpty(result.Files[1].Tags);
    }

    private async Task<TagDetails> UpdateAsync(int id, TagInput input)
    {
        var result = await Tags.UpdateAsync(id, input, TestContext.CancellationToken);
        Assert.AreEqual(TagEditStatus.Ok, result.Status, string.Join("; ", result.Errors?.SelectMany(e => e.Value) ?? []));
        return result.Tag!;
    }

    private async Task<FileTagChangeResult> ChangeAsync(FileTagChange change)
    {
        var (result, errors) = await Tags.ChangeFileTagsAsync(change, TestContext.CancellationToken);
        Assert.IsNotNull(result, string.Join("; ", errors?.SelectMany(e => e.Value) ?? []));
        return result;
    }

    /// <summary>Number of results, reading every page.</summary>
    private async Task<int> CountFoundAsync(string query, string path)
    {
        var count = 0;
        string? after = null;
        do
        {
            var page = (await _services.Get<FileSearchService>().SearchAsync(query, path, after, 500, cancellationToken: TestContext.CancellationToken))!;
            count += page.Items.Count;
            after = page.Next;
        }
        while (after is not null);
        return count;
    }

    private async Task<TagDetails> CreateAsync(TagInput input)
    {
        var result = await Tags.CreateAsync(input, TestContext.CancellationToken);
        Assert.AreEqual(TagEditStatus.Ok, result.Status, string.Join("; ", result.Errors?.SelectMany(e => e.Value) ?? []));
        return result.Tag!;
    }

    private async Task<FileTagChangeResult> TagAsync(string[] paths, params int[] tagIds)
    {
        var (result, errors) = await Tags.ChangeFileTagsAsync(new FileTagChange(paths, Add: tagIds), TestContext.CancellationToken);
        Assert.IsNotNull(result, string.Join("; ", errors?.SelectMany(e => e.Value) ?? []));
        return result;
    }

    private async Task<ScopeTagChangeResult> ChangeScopeAsync(ScopeTagChange change)
    {
        var outcome = await Scope.ChangeAsync(change, TestContext.CancellationToken);
        Assert.AreEqual(ScopeTagStatus.Ok, outcome.Status, string.Join("; ", outcome.Errors?.SelectMany(e => e.Value) ?? []));
        return outcome.Result!;
    }

    private async Task ScanAsync(string relative)
    {
        var progress = new ScanProgress();
        await _services.Get<DirectoryIndexer>().IndexAsync(relative.Length == 0 ? _library.Root : _library.FullPath(relative), true, progress, TestContext.CancellationToken);
        Assert.AreEqual(0, progress.Errors);
    }

    private async Task<string[]> SearchNamesAsync(string query, string path = "") =>
        ((await _services.Get<FileSearchService>().SearchAsync(query, path, null, 100, cancellationToken: TestContext.CancellationToken))
            ?? throw new AssertFailedException("Search scope not found.")).Items.Select(i => i.Name).ToArray();

    private Task<SfeDbContext> CreateDbAsync() =>
        _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(TestContext.CancellationToken);
}
