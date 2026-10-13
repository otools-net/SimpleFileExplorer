using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Search;
using Sfe.Core.Tagging;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Search terms on their own, negated and combined: name, full path (<c>*</c>), exclusion, size, extension, tags (by name,
/// shorthand, alias, through parents, negated, untagged) and alternatives, run against an indexed, tagged
/// <see cref="TestLibrary"/>.
/// </summary>
/// <remarks>
/// Tags of the fixture (<see cref="TagLibraryAsync"/>):
/// <code>
/// Animal ─┬─ Cat ── Kitten (alias "Kitty")
///         └─ Dog
/// Vacation (shorthand "Vac", alias "Holiday trip")
/// Science Fiction
///
/// Media/photo.jpg                  Cat, Vacation
/// Media/Holiday 2020.mp4           Dog, Vacation
/// Media/Episode 2.mkv              Kitten, Science Fiction
/// Media/Episode 10.mkv             Science Fiction
/// RFile1.txt                       Dog
/// RFile2                           Archived (hidden: left out unless the search asks for it)
/// ChildDir/AFile1.txt              Favorite
/// RepeatedDir/ChildDir/AFile1.txt  Cat
/// </code>
/// Sizes: Holiday 2020.mp4 5 MB, Episode 10.mkv 300 KB, Episode 2.mkv 200 KB, photo.jpg 3 KB, RFile2 2 KB,
/// every other file 10 bytes. File names are indexed without their extension.
/// Resolutions (<see cref="SetResolutionsAsync"/>): photo.jpg 4032×3024, Holiday 2020.mp4 1920×1080, Episode 2.mkv
/// 1280×720, Episode 10.mkv unreadable (0×0), every other file unknown; all HR by the default thresholds (photos 1500,
/// videos 720). Durations: Holiday 2020.mp4 1:30:00.4, Episode 2.mkv 25:00, Episode 10.mkv unreadable (0), others unknown.
/// </remarks>
[TestClass]
public sealed class SearchCombinationTests
{
    private const string Photo = "Media/photo.jpg";
    private const string Holiday = "Media/Holiday 2020.mp4";
    private const string Episode2 = "Media/Episode 2.mkv";
    private const string Episode10 = "Media/Episode 10.mkv";
    private const string RFile1 = "RFile1.txt";
    private const string RFile2 = "RFile2";
    private const string ChildAFile1 = "ChildDir/AFile1.txt";
    private const string ChildAFile2 = "ChildDir/AFile2";
    private const string RepeatedAFile1 = "RepeatedDir/ChildDir/AFile1.txt";
    private const string RepeatedAFile2 = "RepeatedDir/ChildDir/AFile2";
    private const string Special = "SpecialDir/" + TestLibrary.SpecialFolder;
    private const string Done = "100%_done[1].txt";
    private const string RDFile1 = "RepeatedDir/RDFile1.txt";
    private const string RDFile2 = "RepeatedDir/RDFile2.txt";
    private const string RepeatedRDFile1 = "RepeatedDir/RepeatedDir/RDFile1.txt";
    private const string RepeatedRDFile2 = "RepeatedDir/RepeatedDir/RDFile2.txt";
    private const string SDEnglish = "SpecialDir/SDEnglish.con";
    private const string SDSpanish = "SpecialDir/SDEspañol";
    private const string SDRussian = "SpecialDir/SDРусский.txt";
    private const string SFEnglish = Special + "/SFEnglish.con";
    private const string SFSpanish = Special + "/SFEspañol";
    private const string SFRussian = Special + "/SFРусский.txt";

    /// <summary>The ten .txt files, all 10 bytes and untagged but for RFile1 (Dog), ChildDir/AFile1 (Favorite), RepeatedDir/ChildDir/AFile1 (Cat).</summary>
    private const string TextFiles = RFile1 + "|" + Done + "|" + ChildAFile1 + "|" + RDFile1 + "|" + RDFile2 + "|" + RepeatedAFile1 + "|"
        + RepeatedRDFile1 + "|" + RepeatedRDFile2 + "|" + SDRussian + "|" + SFRussian;

    /// <summary>The visible files without an extension (RFile2 has none too, but is hidden).</summary>
    private const string NoExtension = ChildAFile2 + "|" + RepeatedAFile2 + "|" + SDSpanish + "|" + SFSpanish;

    private const string MediaFiles = Photo + "|" + Holiday + "|" + Episode2 + "|" + Episode10;

    /// <summary>The 20 files a search can find without naming a hidden tag.</summary>
    private const string Visible = TextFiles + "|" + NoExtension + "|" + MediaFiles + "|" + SDEnglish + "|" + SFEnglish;

    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
        var progress = new ScanProgress();
        await _services.Get<DirectoryIndexer>().IndexAsync(_library.Root, true, progress, TestContext.CancellationToken);
        Assert.AreEqual(0, progress.Errors);
        await TagLibraryAsync();
        await SetResolutionsAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    /* Expected results are paths separated by '|', in any order; "" means no result. */

    [TestMethod]
    [DataRow("episode", Episode10 + "|" + Episode2)]
    [DataRow("EPISODE", Episode10 + "|" + Episode2)]
    [DataRow("episode 1", Episode10)]
    [DataRow("episode 10", Episode10)]
    [DataRow("afile1", ChildAFile1 + "|" + RepeatedAFile1)]
    [DataRow("holiday 2020", Holiday)]
    [DataRow("\"holiday 2020\"", Holiday)]
    [DataRow("\"2020 holiday\"", "")]
    [DataRow("2020 holiday", Holiday)]
    [DataRow("SFEnglish", Special + "/SFEnglish.con")]
    [DataRow("nothing-like-this", "")]
    public Task FindsByName(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("txt", "")]
    [DataRow("mkv", "")]
    [DataRow("e:mkv", Episode10 + "|" + Episode2)]
    [DataRow("e:.MKV", Episode10 + "|" + Episode2)]
    [DataRow("*.mkv", Episode10 + "|" + Episode2)]
    [DataRow("e:mkv e:jpg", Episode10 + "|" + Episode2 + "|" + Photo)]
    [DataRow("e:mkv, e:jpg", Episode10 + "|" + Episode2 + "|" + Photo)]
    [DataRow("e:con", "SpecialDir/SDEnglish.con|" + Special + "/SFEnglish.con")]
    [DataRow("e:avi", "")]
    public Task NameTermsIgnoreTheExtensionWhichHasItsOwnFilter(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("type:image", Photo)]
    [DataRow("type:VIDEO", Holiday + "|" + Episode2 + "|" + Episode10)]
    [DataRow("type:audio", "")]
    [DataRow("type:other", TextFiles + "|" + NoExtension + "|" + SDEnglish + "|" + SFEnglish)]
    [DataRow("type:image type:video", MediaFiles)]
    [DataRow("type:image, type:video", MediaFiles)]
    [DataRow("type:video episode", Episode2 + "|" + Episode10)]
    [DataRow("type:video -e:mkv", Holiday)]
    [DataRow("type:video -type:video", "")]
    [DataRow("type:other e:con", SDEnglish + "|" + SFEnglish)]
    [DataRow("type:image e:mkv", "")]
    [DataRow("holiday type:nothing", Holiday)]
    public Task FindsByKindOfFile(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow(SortField.Resolution, false, Episode2 + "|" + Holiday + "|" + Photo)]
    [DataRow(SortField.Resolution, true, Photo + "|" + Holiday + "|" + Episode2)]
    [DataRow(SortField.Duration, false, Episode2 + "|" + Holiday)]
    [DataRow(SortField.Duration, true, Holiday + "|" + Episode2)]
    public async Task SortsByDurationAndResolutionWithUnknownValuesLast(SortField sort, bool descending, string known)
    {
        // Known values in order, then every other file (unknown or unreadable) by path, whichever the direction.
        var search = _services.Get<FileSearchService>();
        var all = (await search.SearchAsync("", "", null, 100, sort, descending, TestContext.CancellationToken))!.Items.Select(i => i.Path).ToArray();
        var expected = known.Split('|');
        CollectionAssert.AreEqual(expected, all.Take(expected.Length).ToArray());
        var rest = all.Skip(expected.Length).ToArray();
        var byPath = (await search.SearchAsync("", "", null, 100, SortField.Path, false, TestContext.CancellationToken))!.Items.Select(i => i.Path);
        CollectionAssert.AreEqual(byPath.Where(rest.Contains).ToArray(), rest);
        Assert.HasCount(20, all);
        CollectionAssert.Contains(rest, Episode10, "Unreadable (0) counts as unknown.");

        // Pages of every size give the same order, across the switch from known to unknown values too.
        foreach (var size in new[] { 1, 2, 3, 5 })
        {
            var paged = new List<string>();
            string? after = null;
            do
            {
                var page = (await search.SearchAsync("", "", after, size, sort, descending, TestContext.CancellationToken))!;
                paged.AddRange(page.Items.Select(i => i.Path));
                after = page.Next;
            }
            while (after is not null);
            CollectionAssert.AreEqual(all, paged.ToArray(), $"Pages of {size}");
        }
    }

    [TestMethod]
    [DataRow("episode -10", Episode2)]
    [DataRow("episode -EPISODE", "")]
    [DataRow("afile -2", ChildAFile1 + "|" + RepeatedAFile1)]
    [DataRow("afile -1 -2", "")]
    [DataRow("rdfile -1", "RepeatedDir/RDFile2.txt|RepeatedDir/RepeatedDir/RDFile2.txt")]
    [DataRow("holiday -\"2020\"", "")]
    [DataRow("photo -nothing", Photo)]
    public Task ExcludesNames(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    public async Task AnExclusionAloneListsEverythingElse()
    {
        // RFile2 has a hidden tag: left out whatever the terms.
        CollectionAssert.AreEquivalent(
            new[] { "100%_done[1].txt", "SpecialDir/SDEnglish.con", "SpecialDir/SDEspañol", "SpecialDir/SDРусский.txt",
                Special + "/SFEnglish.con", Special + "/SFEspañol", Special + "/SFРусский.txt", Holiday, Episode10, Episode2, Photo },
            await SearchPathsAsync("-file"));
    }

    [TestMethod]
    [DataRow("*RepeatedDir AFile", RepeatedAFile1 + "|" + RepeatedAFile2)]
    [DataRow("*childdir -repeateddir", ChildAFile1 + "|" + ChildAFile2)]
    [DataRow("*media -episode", Holiday + "|" + Photo)]
    [DataRow("*RepeatedDir RDFile1", "RepeatedDir/RDFile1.txt|RepeatedDir/RepeatedDir/RDFile1.txt")]
    [DataRow("*specialdir e:txt", "SpecialDir/SDРусский.txt|" + Special + "/SFРусский.txt")]
    [DataRow("RepeatedDir AFile", "")]
    [DataRow("*childdir, *media e:jpg", ChildAFile1 + "|" + ChildAFile2 + "|" + RepeatedAFile1 + "|" + RepeatedAFile2 + "|" + Photo)]
    public Task LeadingStarMatchesTheWholePathInEveryAlternative(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("s:>1mb", Holiday)]
    [DataRow("s:>=5mb", Holiday)]
    [DataRow("s:>5mb", "")]
    [DataRow("s:5", Holiday)]
    [DataRow("s:5mb", Holiday)]
    [DataRow("s:=5242880b", Holiday)]
    [DataRow("s:>=200kb s:<=300kb", Episode10 + "|" + Episode2)]
    [DataRow("s:>200kb s:<300kb", "")]
    [DataRow("s:>200kb", Holiday + "|" + Episode10)]
    [DataRow("s:=3kb", Photo)]
    [DataRow("s:3kb", Photo)]
    [DataRow("s:>1kb s:<1mb", Episode10 + "|" + Episode2 + "|" + Photo)]
    [DataRow("s:>1kb s:<1mb -episode", Photo)]
    [DataRow("s:<1mb e:mkv", Episode10 + "|" + Episode2)]
    [DataRow("s:<1kb *media", "")]
    [DataRow("photo s:big", Photo)]
    public Task FiltersBySize(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("w:>=1920", Photo + "|" + Holiday)]
    [DataRow("w:1920", Holiday)]
    [DataRow("W:=1280", Episode2)]
    [DataRow("w:>4032", "")]
    [DataRow("h:<1080", Episode2)]
    [DataRow("h:<=1080", Holiday + "|" + Episode2)]
    [DataRow("r:>720x1024", Photo + "|" + Holiday)]
    [DataRow("r:1920x1080", Holiday)]
    [DataRow("r:>=1920X1080", Photo + "|" + Holiday)]
    [DataRow("r:<1920x1080", Episode2)]
    [DataRow("r:<=1920x1080", Holiday + "|" + Episode2)]
    [DataRow("w:>2000, h:720", Photo + "|" + Episode2)]
    [DataRow("episode w:>0", Episode2)]
    [DataRow("e:mp4 h:>=1080", Holiday)]
    [DataRow("*media w:<2000 -holiday", Episode2)]
    [DataRow("photo w:big", Photo)]
    [DataRow("holiday r:1920", Holiday)]
    [DataRow("w:<1", "")]
    public Task FiltersByResolution(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("d:>1h", Holiday)]
    [DataRow("D:>=25m", Holiday + "|" + Episode2)]
    [DataRow("d:25", Episode2)]
    [DataRow("d:1500s", Episode2)]
    [DataRow("d:=25:00", Episode2)]
    [DataRow("d:1.5h", Holiday)]
    [DataRow("d:1:30:00", Holiday)]
    [DataRow("d:=1:30:00", Holiday)]
    [DataRow("d:=1:30:01", "")]
    [DataRow("d:<30m", Episode2)]
    [DataRow("d:<1s", "")]
    [DataRow("d:3h", "")]
    [DataRow("e:mkv d:>0", Episode2)]
    [DataRow("episode, d:>1h", Episode10 + "|" + Episode2 + "|" + Holiday)]
    [DataRow("holiday d:long", Holiday)]
    [DataRow("*media -d:>1h", Photo + "|" + Episode2 + "|" + Episode10)]
    [DataRow("*media -d:>0", Photo + "|" + Episode10)]
    [DataRow("*media -d:25 e:mkv", Episode10)]
    public Task FiltersByDuration(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("r:hr", Photo + "|" + Holiday + "|" + Episode2)]
    [DataRow("r:HR e:mkv", Episode2)]
    [DataRow("r:lr", "")]
    [DataRow("*media -r:hr", Episode10)]
    [DataRow("r:hr, r:lr", Photo + "|" + Holiday + "|" + Episode2)]
    public Task FiltersByHighOrLowResolution(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    public async Task HighResolutionFollowsTheThresholdOfEachKind()
    {
        // The thresholds of the HR/LR marks, marks off: a 1280×720 video and a 4032×3024 photo become LR.
        await using var services = await CoreServices.CreateAsync(_library, s =>
        {
            s["Sfe:Browse:VideoHighResolutionPixels"] = "1000";
            s["Sfe:Browse:ImageHighResolutionPixels"] = "3500";
        });
        async Task<string[]> FindAsync(string query) =>
            (await services.Get<FileSearchService>().SearchAsync(query, "", null, 100, cancellationToken: TestContext.CancellationToken))!
                .Items.Select(i => i.Path).ToArray();

        CollectionAssert.AreEquivalent(new[] { Holiday }, await FindAsync("r:hr"));
        CollectionAssert.AreEquivalent(new[] { Photo, Episode2 }, await FindAsync("r:lr"));
        CollectionAssert.AreEquivalent(new[] { Photo, Episode2, Episode10 }, await FindAsync("*media -r:hr"));
    }

    [TestMethod]
    public async Task ResultsCarryTheDurationOfVideos()
    {
        var items = (await _services.Get<FileSearchService>().SearchAsync("*media", "", null, 100, cancellationToken: TestContext.CancellationToken))!.Items;

        Assert.AreEqual(5400.4, items.Single(i => i.Path == Holiday).Duration);
        Assert.AreEqual(1500, items.Single(i => i.Path == Episode2).Duration);
        Assert.IsNull(items.Single(i => i.Path == Episode10).Duration, "Unreadable: not listed as 0.");
        Assert.IsNull(items.Single(i => i.Path == Photo).Duration);
    }

    [TestMethod]
    [DataRow("*media -e:mkv", Holiday + "|" + Photo)]
    [DataRow("*media -e:mkv -e:jpg", Holiday)]
    [DataRow("*media -E:.MKV, e:jpg", Holiday + "|" + Photo)]
    [DataRow("episode -e:mkv", "")]
    [DataRow("*media -s:>1mb", Episode10 + "|" + Episode2 + "|" + Photo)]
    [DataRow("*media -s:5mb", Episode10 + "|" + Episode2 + "|" + Photo)]
    [DataRow("*media -s:<1mb -s:>4mb", "")]
    [DataRow("*media -w:>=1920", Episode10 + "|" + Episode2)]
    [DataRow("*media -h:720", Photo + "|" + Holiday + "|" + Episode10)]
    [DataRow("*media -r:1920x1080", Photo + "|" + Episode2 + "|" + Episode10)]
    [DataRow("*media -r:>1000x1000", Episode2 + "|" + Episode10)]
    [DataRow("*media -r:>2000x1000", Episode2 + "|" + Episode10 + "|" + Holiday)]
    [DataRow("*media -w:1920 w:>0", Photo + "|" + Episode2)]
    [DataRow("rfile -w:1920", RFile1)]
    [DataRow("photo -s:big", Photo)]
    public Task NegatesSizeExtensionAndResolutionFilters(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    public async Task EveryTermAndItsNegationSplitTheVisibleFiles()
    {
        // The rule for "-": it finds exactly the files the term doesn't (unknown resolutions and durations included).
        var all = await SearchPathsAsync("s:>=0b");
        string[] terms = ["episode", "e:mkv", "e:txt", "s:<1mb", "s:5mb", "s:=10b", "w:>=1920", "w:<2000", "h:720", "r:1920x1080",
            "r:<=1920x1080", "r:hr", "r:lr", "d:>1h", "d:25m", "d:<=1:30:00", "type:image", "type:video", "type:audio", "type:other", "t:animal", "t:vac", "special:untagged"];
        foreach (var term in terms)
        {
            var found = await SearchPathsAsync(term);
            var negated = await SearchPathsAsync("-" + term);
            CollectionAssert.AreEquivalent(all, found.Concat(negated).ToArray(), $"“{term}” and “-{term}”");
            Assert.IsFalse(found.Intersect(negated).Any(), $"“{term}” and “-{term}” overlap");
        }
    }

    [TestMethod]
    public async Task SizeFiltersCountEveryMatchingFile()
    {
        // 16 files of 10 bytes; RFile2 (2 KB) is the only other one under 1 MB besides the media, and it is hidden.
        Assert.HasCount(16, await SearchPathsAsync("s:<1kb"));
        Assert.HasCount(16, await SearchPathsAsync("s:=10b"));
        Assert.HasCount(16, await SearchPathsAsync("s:<=10b"));
        Assert.HasCount(19, await SearchPathsAsync("s:<1mb"));
        Assert.HasCount(20, await SearchPathsAsync("s:>=0b"));
    }

    [TestMethod]
    [DataRow("t:cat", Photo + "|" + RepeatedAFile1 + "|" + Episode2)]
    [DataRow("tag:CAT", Photo + "|" + RepeatedAFile1 + "|" + Episode2)]
    [DataRow("t:kitten", Episode2)]
    [DataRow("t:kitty", Episode2)]
    [DataRow("t:dog", Holiday + "|" + RFile1)]
    [DataRow("t:animal", Photo + "|" + RepeatedAFile1 + "|" + Episode2 + "|" + Holiday + "|" + RFile1)]
    [DataRow("t:vacation", Photo + "|" + Holiday)]
    [DataRow("t:vac", Photo + "|" + Holiday)]
    [DataRow("t:\"holiday trip\"", Photo + "|" + Holiday)]
    [DataRow("t:holiday_trip", Photo + "|" + Holiday)]
    [DataRow("t:\"science fiction\"", Episode10 + "|" + Episode2)]
    [DataRow("t:Science_Fiction", Episode10 + "|" + Episode2)]
    [DataRow("t:science", "")]
    [DataRow("t:nobody", "")]
    [DataRow("t:favorite", ChildAFile1)]
    [DataRow("t:\"meta tags\"", ChildAFile1 + "|" + RFile2)]
    public Task FindsByTagIncludingTheTagsInheritingFromIt(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("t:cat t:vacation", Photo)]
    [DataRow("t:cat, t:vacation", Photo + "|" + RepeatedAFile1 + "|" + Episode2 + "|" + Holiday)]
    [DataRow("t:animal -t:cat", Holiday + "|" + RFile1)]
    [DataRow("t:animal -t:kitten", Photo + "|" + RepeatedAFile1 + "|" + Holiday + "|" + RFile1)]
    [DataRow("t:\"science fiction\" -t:kitty", Episode10)]
    [DataRow("-t:animal e:mkv", Episode10)]
    [DataRow("*media -t:animal -t:vacation", Episode10)]
    [DataRow("-t:nobody episode", Episode10 + "|" + Episode2)]
    [DataRow("t:cat -t:cat", "")]
    [DataRow("t:dog t:cat", "")]
    public Task CombinesAndNegatesTags(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("special:untagged e:mkv", "")]
    [DataRow("*childdir special:untagged", ChildAFile2 + "|" + RepeatedAFile2)]
    [DataRow("special:untagged afile", ChildAFile2 + "|" + RepeatedAFile2)]
    [DataRow("*media -special:untagged", Photo + "|" + Holiday + "|" + Episode2 + "|" + Episode10)]
    [DataRow("-special:untagged -t:animal", Episode10 + "|" + ChildAFile1)]
    [DataRow("special:untagged t:cat", "")]
    [DataRow("special:untagged e:mkv, t:cat e:jpg", Photo)]
    public Task FindsUntaggedFiles(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    public async Task UntaggedAndTaggedSplitTheVisibleFiles()
    {
        var tagged = await SearchPathsAsync("-special:untagged");
        var untagged = await SearchPathsAsync("special:untagged");

        CollectionAssert.AreEquivalent(new[] { Photo, Holiday, Episode2, Episode10, RFile1, ChildAFile1, RepeatedAFile1 }, tagged);
        Assert.HasCount(20 - tagged.Length, untagged);
        Assert.IsFalse(tagged.Intersect(untagged).Any());
    }

    [TestMethod]
    [DataRow("rfile", RFile1)]
    [DataRow("s:2kb", "")]
    [DataRow("s:2kb t:archived", RFile2)]
    [DataRow("t:archived", RFile2)]
    [DataRow("t:archive", RFile2)]
    [DataRow("rfile, t:archived", RFile1 + "|" + RFile2)]
    [DataRow("rfile -t:archived", RFile1)]
    [DataRow("-special:untagged rfile", RFile1)]
    public Task HiddenTagsOnlyShowUpWhenAsked(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("episode s:>250kb t:\"science fiction\"", Episode10)]
    [DataRow("episode s:<250kb t:\"science fiction\"", Episode2)]
    [DataRow("*media -t:animal s:<1mb", Episode10)]
    [DataRow("afile1 -t:cat, e:mp4", ChildAFile1 + "|" + Holiday)]
    [DataRow("*repeateddir t:cat s:<1kb", RepeatedAFile1)]
    [DataRow("t:favorite, t:dog -rfile", ChildAFile1 + "|" + Holiday)]
    [DataRow("t:vac s:>1mb, t:kitty e:mkv, special:untagged s:2kb", Holiday + "|" + Episode2)]
    [DataRow("photo e:jpg s:3kb t:cat t:vac -t:dog -special:untagged", Photo)]
    [DataRow("photo e:jpg s:3kb t:cat t:vac -t:dog special:untagged", "")]
    [DataRow("photo e:mkv", "")]
    [DataRow("episode e:mkv -2 -t:nobody s:>=300kb", Episode10)]
    [DataRow("*Media e:mp4 holiday, *Media e:jpg photo", Holiday + "|" + Photo)]
    public Task CombinesEveryKindOfTerm(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    public async Task CombinationsGiveTheIntersectionOfTheirTerms()
    {
        // Every pair of terms (AND) finds exactly the files both terms find on their own; "a, b" (OR) their union.
        // ("*" is left out: it switches the whole query to full paths; so is a second "e:", OR-ed with the first.)
        string[] terms = ["episode", "-episode", "photo", "-afile", "e:mkv", "s:<1mb", "s:>250kb", "t:animal",
            "-t:cat", "t:vac", "special:untagged", "-special:untagged"];
        var alone = new Dictionary<string, string[]>();
        foreach (var term in terms)
            alone[term] = await SearchPathsAsync(term);

        foreach (var a in terms)
        {
            foreach (var b in terms)
            {
                CollectionAssert.AreEquivalent(alone[a].Intersect(alone[b]).ToArray(), await SearchPathsAsync($"{a} {b}"), $"“{a} {b}”");
                CollectionAssert.AreEquivalent(alone[a].Union(alone[b]).ToArray(), await SearchPathsAsync($"{a}, {b}"), $"“{a}, {b}”");
            }
        }
    }

    [TestMethod]
    [DataRow("episode, photo", Episode10 + "|" + Episode2 + "|" + Photo)]
    [DataRow("episode,photo", Episode10 + "|" + Episode2 + "|" + Photo)]
    [DataRow(", ,episode,, ", Episode10 + "|" + Episode2)]
    [DataRow("episode, episode", Episode10 + "|" + Episode2)]
    [DataRow("episode 2, episode 10", Episode2 + "|" + Episode10)]
    [DataRow("rfile1, afile1, sdenglish", RFile1 + "|" + ChildAFile1 + "|" + RepeatedAFile1 + "|" + SDEnglish)]
    [DataRow("nothing-like-this, photo", Photo)]
    [DataRow("e:mkv, s:>1mb", Episode10 + "|" + Episode2 + "|" + Holiday)]
    [DataRow("t:dog, e:jpg", Holiday + "|" + RFile1 + "|" + Photo)]
    [DataRow("t:vac, t:kitty, t:dog", Photo + "|" + Holiday + "|" + Episode2 + "|" + RFile1)]
    [DataRow("h:1080, h:720, h:3024", Holiday + "|" + Episode2 + "|" + Photo)]
    // AND binds tighter than OR: "a b, c" is "(a and b) or c".
    [DataRow("e:mkv s:>250kb, e:mp4", Episode10 + "|" + Holiday)]
    [DataRow("e:mkv, e:mp4 s:>1mb", Episode10 + "|" + Episode2 + "|" + Holiday)]
    [DataRow("e:mkv, e:mp4 s:<1mb", Episode10 + "|" + Episode2)]
    // A term or its negation: every visible file.
    [DataRow("-episode, episode", Visible)]
    [DataRow("t:cat, -t:cat", Visible)]
    [DataRow("e:txt, -e:txt", Visible)]
    [DataRow("w:>0, -w:>0", Visible)]
    [DataRow("s:<1kb, s:>=1kb", Visible)]
    [DataRow("t:favorite, -t:favorite", Visible)]
    [DataRow("special:untagged, -special:untagged", Visible)]
    public Task AlternativesFindTheFilesOfAnyOfThem(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("e:TXT", TextFiles)]
    [DataRow("e:.txt", TextFiles)]
    [DataRow("e:..txt", TextFiles)]
    [DataRow("e:tx", "")]
    [DataRow("e:xt", "")]
    [DataRow("e:txt e:con", TextFiles + "|" + SDEnglish + "|" + SFEnglish)]
    [DataRow("e:mp4 e:mkv e:jpg", MediaFiles)]
    [DataRow("e:mp4 e:mkv e:jpg -t:vacation", Episode2 + "|" + Episode10)]
    [DataRow("afile e:txt", ChildAFile1 + "|" + RepeatedAFile1)]
    [DataRow("afile -e:txt", ChildAFile2 + "|" + RepeatedAFile2)]
    [DataRow("sd e:con", SDEnglish)]
    [DataRow("e:con -english", "")]
    [DataRow("e:txt e:con -rfile -afile -rdfile", Done + "|" + SDRussian + "|" + SFRussian + "|" + SDEnglish + "|" + SFEnglish)]
    [DataRow("e:txt -e:txt", "")]
    [DataRow("e:txt e:con -e:txt", SDEnglish + "|" + SFEnglish)]
    [DataRow("e:mkv -e:mp4", Episode10 + "|" + Episode2)]
    [DataRow("-e:txt -e:con -e:mkv -e:mp4 -e:jpg", NoExtension)]
    [DataRow("-e:txt -e:con -e:mkv -e:mp4 -e:jpg, e:jpg", NoExtension + "|" + Photo)]
    [DataRow("-e:txt -e:con -e:mkv -e:mp4 -e:jpg español", SDSpanish + "|" + SFSpanish)]
    public Task CombinesExtensions(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("100%", Done)]
    [DataRow("%", Done)]
    [DataRow("_", Done)]
    [DataRow("%_", Done)]
    [DataRow("[", Done)]
    [DataRow("[1]", Done)]
    [DataRow("done[1", Done)]
    [DataRow("100%_done[1] e:txt", Done)]
    [DataRow("-% -_ e:txt -rfile -afile -rdfile", SDRussian + "|" + SFRussian)]
    [DataRow("Русский", SDRussian + "|" + SFRussian)]
    [DataRow("español", SDSpanish + "|" + SFSpanish)]
    [DataRow("Español, Русский", SDSpanish + "|" + SFSpanish + "|" + SDRussian + "|" + SFRussian)]
    [DataRow("Русский -sf", SDRussian)]
    [DataRow("*\"" + TestLibrary.SpecialFolder + "\"", SFEnglish + "|" + SFSpanish + "|" + SFRussian)]
    [DataRow("*\"" + TestLibrary.SpecialFolder + "\" -e:txt -e:con", SFSpanish)]
    public Task MatchesWildcardAndUnicodeCharactersLiterally(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("*childdir", ChildAFile1 + "|" + ChildAFile2 + "|" + RepeatedAFile1 + "|" + RepeatedAFile2)]
    [DataRow("*childdir e:txt", ChildAFile1 + "|" + RepeatedAFile1)]
    [DataRow("*repeateddir -childdir", RDFile1 + "|" + RDFile2 + "|" + RepeatedRDFile1 + "|" + RepeatedRDFile2)]
    [DataRow("*rdfile", RDFile1 + "|" + RDFile2 + "|" + RepeatedRDFile1 + "|" + RepeatedRDFile2)]
    [DataRow("*.txt", TextFiles)]
    [DataRow("*.con, .mkv", SDEnglish + "|" + SFEnglish + "|" + Episode10 + "|" + Episode2)]
    [DataRow("*español", SDSpanish + "|" + SFSpanish)]
    [DataRow("*media t:vac", Photo + "|" + Holiday)]
    [DataRow("*media -t:vac -e:mkv", "")]
    [DataRow("*media -s:<1mb -w:>0", "")]
    [DataRow("*specialdir -e:txt -e:con", SDSpanish + "|" + SFSpanish)]
    [DataRow("*media, childdir", MediaFiles + "|" + ChildAFile1 + "|" + ChildAFile2 + "|" + RepeatedAFile1 + "|" + RepeatedAFile2)]
    [DataRow("*media, childdir special:untagged", MediaFiles + "|" + ChildAFile2 + "|" + RepeatedAFile2)]
    public Task MatchesFullPathsWithEveryOtherTerm(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("e:mkv s:>250kb", Episode10)]
    [DataRow("e:mkv -s:>250kb", Episode2)]
    [DataRow("s:200kb", Episode2)]
    [DataRow("s:300kb", Episode10)]
    [DataRow("s:=10b e:txt", TextFiles)]
    [DataRow("s:=10b -e:txt", ChildAFile2 + "|" + RepeatedAFile2 + "|" + SDEnglish + "|" + SDSpanish + "|" + SFEnglish + "|" + SFSpanish)]
    [DataRow("s:>10b", MediaFiles)]
    [DataRow("s:>10b, t:archived", MediaFiles + "|" + RFile2)]
    [DataRow("s:>=3kb s:<=3kb", Photo)]
    [DataRow("s:>1gb", "")]
    [DataRow("s:<0", "")]
    [DataRow("-s:<0", Visible)]
    [DataRow("s:>=0b -s:>=0b", "")]
    [DataRow("-s:=10b -s:>1mb", Episode10 + "|" + Episode2 + "|" + Photo)]
    [DataRow("-s:=10b -s:>1mb -e:mkv", Photo)]
    public Task CombinesSizes(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("w:>0 h:>0", Photo + "|" + Holiday + "|" + Episode2)]
    [DataRow("w:>0, s:>250kb", Photo + "|" + Holiday + "|" + Episode2 + "|" + Episode10)]
    [DataRow("r:1280x720, r:1920x1080", Episode2 + "|" + Holiday)]
    [DataRow("w:>=1280 w:<=1920", Holiday + "|" + Episode2)]
    [DataRow("w:>=1280 -w:1920 w:<=1920", Episode2)]
    [DataRow("h:>=720 -e:mp4", Photo + "|" + Episode2)]
    [DataRow("*media -w:>0", Episode10)]
    [DataRow("-w:>0 *media", "")]   // "*" past the start is a character, and no name has one
    [DataRow("w:=0", "")]
    [DataRow("h:0, w:<=0", "")]
    [DataRow("r:>=1280x720 s:<1mb", Photo + "|" + Episode2)]
    [DataRow("r:<=4032x3024 r:>=4032x3024", Photo)]
    [DataRow("-r:1280x720 -r:1920x1080 w:>0", Photo)]
    [DataRow("w:>1000 -h:>1000", Episode2)]
    [DataRow("r:>1000x700 -t:dog", Photo + "|" + Episode2)]
    [DataRow("w:>0 -t:animal", "")]
    public Task CombinesResolutions(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("t:animal e:mkv", Episode2)]
    [DataRow("t:animal -e:mkv -e:txt", Photo + "|" + Holiday)]
    [DataRow("t:vacation w:>=1920 -s:>1mb", Photo)]
    [DataRow("t:\"science fiction\" -w:>0", Episode10)]
    [DataRow("t:animal, t:\"science fiction\"", Photo + "|" + RepeatedAFile1 + "|" + Episode2 + "|" + Holiday + "|" + RFile1 + "|" + Episode10)]
    [DataRow("t:kitten, t:dog -e:mp4", Episode2 + "|" + RFile1)]
    [DataRow("t:cat afile", RepeatedAFile1)]
    [DataRow("t:favorite, t:cat afile", ChildAFile1 + "|" + RepeatedAFile1)]
    [DataRow("t:animal t:vacation -t:cat", Holiday)]
    [DataRow("t:cat t:kitten", Episode2)]
    [DataRow("t:cat t:cat", Photo + "|" + RepeatedAFile1 + "|" + Episode2)]
    [DataRow("t:animal -t:dog -t:cat", "")]
    [DataRow("-t:animal -special:untagged", Episode10 + "|" + ChildAFile1)]
    [DataRow("special:untagged e:txt", Done + "|" + RDFile1 + "|" + RDFile2 + "|" + RepeatedRDFile1 + "|" + RepeatedRDFile2 + "|" + SDRussian + "|" + SFRussian)]
    [DataRow("special:untagged -e:txt", ChildAFile2 + "|" + RepeatedAFile2 + "|" + SDEnglish + "|" + SDSpanish + "|" + SFEnglish + "|" + SFSpanish)]
    [DataRow("-special:untagged -e:txt", MediaFiles)]
    [DataRow("special:untagged w:>0", "")]
    [DataRow("special:untagged -special:untagged", "")]
    public Task CombinesTagsWithOtherTerms(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("t:\"meta tags\" -t:archived", ChildAFile1)]
    [DataRow("e:txt, t:archived", TextFiles + "|" + RFile2)]
    [DataRow("s:>1kb s:<4kb, t:archived", Photo + "|" + RFile2)]
    [DataRow("t:archived -s:2kb", "")]
    [DataRow("t:archived -e:txt", RFile2)]
    [DataRow("-t:archived", Visible)]
    [DataRow("-e:txt -e:con -e:mkv -e:mp4 -e:jpg, t:archived", NoExtension + "|" + RFile2)]
    public Task HiddenTagsShowUpInEveryAlternativeOnceOneAsksForThem(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("PHOTO", Photo)]
    [DataRow("T:CAT E:JPG", Photo)]
    [DataRow("S:>1MB", Holiday)]
    [DataRow("W:>=1920 H:>=1080", Photo + "|" + Holiday)]
    [DataRow("R:1920X1080", Holiday)]
    [DataRow("*MEDIA -E:MKV", Photo + "|" + Holiday)]
    [DataRow("SPECIAL:UNTAGGED E:CON", SDEnglish + "|" + SFEnglish)]
    [DataRow("-T:ANIMAL -SPECIAL:UNTAGGED", Episode10 + "|" + ChildAFile1)]
    public Task PrefixesAndValuesIgnoreCase(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("\"episode 2\"", Episode2)]
    [DataRow("\"episode 2\", photo", Episode2 + "|" + Photo)]
    [DataRow("\"episode, photo\"", "")]
    [DataRow("-\"episode 1\" episode", Episode2)]
    [DataRow("\"episode\" \"2\"", Episode2)]
    [DataRow("t:\"science fiction\", t:dog", Episode10 + "|" + Episode2 + "|" + Holiday + "|" + RFile1)]
    [DataRow("t:\"science fiction\" \"episode 1\"", Episode10)]
    // A quote before the prefix makes the term a plain word.
    [DataRow("\"e:mkv\"", "")]
    [DataRow("\"t:cat\"", "")]
    [DataRow("\"-episode\"", "")]
    [DataRow("episode episode", Episode10 + "|" + Episode2)]
    [DataRow("e:mkv e:mkv", Episode10 + "|" + Episode2)]
    public Task QuotesAndRepeatedTerms(string query, string expected) => AssertFindsAsync(query, expected);

    [TestMethod]
    [DataRow("Media", "-e:mkv", Photo + "|" + Holiday)]
    [DataRow("Media", "e:mkv, t:vac", MediaFiles)]
    [DataRow("Media", "*media -episode", Photo + "|" + Holiday)]
    [DataRow("Media", "w:>0 -t:animal", "")]
    [DataRow("RepeatedDir", "afile, rdfile1", RepeatedAFile1 + "|" + RepeatedAFile2 + "|" + RDFile1 + "|" + RepeatedRDFile1)]
    [DataRow("RepeatedDir/RepeatedDir", "rdfile -2", RepeatedRDFile1)]
    [DataRow("SpecialDir", "-e:txt", SDEnglish + "|" + SDSpanish + "|" + SFEnglish + "|" + SFSpanish)]
    [DataRow(Special, "e:con, e:txt", SFEnglish + "|" + SFRussian)]
    [DataRow("ChildDir", "special:untagged", ChildAFile2)]
    [DataRow("ChildDir", "t:cat", "")]
    [DataRow("ChildDir", "t:cat, t:favorite", ChildAFile1)]
    public Task EveryCombinationIsScopedToTheFolder(string path, string query, string expected) => AssertFindsAsync(query, expected, path);

    [TestMethod]
    public async Task ExclusionsAreTheComplementOfAlternatives()
    {
        // De Morgan: "-a -b" finds exactly the visible files "a, b" doesn't.
        var visible = Visible.Split('|');
        string[] terms = ["episode", "afile", "e:mkv", "e:txt", "s:<1mb", "s:=10b", "w:>=1920", "h:<1080", "r:1920x1080", "t:animal", "t:vac",
            "special:untagged"];
        foreach (var a in terms)
        {
            foreach (var b in terms)
            {
                var either = await SearchPathsAsync($"{a}, {b}");
                CollectionAssert.AreEquivalent(visible.Except(either).ToArray(), await SearchPathsAsync($"-{a} -{b}"), $"“-{a} -{b}”");
            }
        }
    }

    [TestMethod]
    public async Task ThreeTermsGiveTheIntersectionOfAllThree()
    {
        string[] terms = ["episode", "-afile", "e:mkv", "-e:txt", "s:<1mb", "-s:=10b", "w:>0", "-h:1080", "t:animal", "-t:vac", "special:untagged"];
        var alone = new Dictionary<string, string[]>();
        foreach (var term in terms)
            alone[term] = await SearchPathsAsync(term);

        for (var i = 0; i < terms.Length; i++)
        {
            for (var j = i + 1; j < terms.Length; j++)
            {
                for (var k = j + 1; k < terms.Length; k++)
                {
                    var (a, b, c) = (terms[i], terms[j], terms[k]);
                    CollectionAssert.AreEquivalent(alone[a].Intersect(alone[b]).Intersect(alone[c]).ToArray(), await SearchPathsAsync($"{a} {b} {c}"), $"“{a} {b} {c}”");
                    CollectionAssert.AreEquivalent(alone[a].Union(alone[b].Intersect(alone[c])).ToArray(), await SearchPathsAsync($"{a}, {b} {c}"), $"“{a}, {b} {c}”");
                }
            }
        }
    }

    [TestMethod]
    [DataRow("e:txt, e:con, -s:<1kb")]
    [DataRow("-t:animal -e:txt")]
    [DataRow("*dir, w:>0")]
    [DataRow("-w:1920 -special:untagged, s:=10b e:con")]
    public async Task CombinedSearchesPageThroughEveryMatchInEverySort(string query)
    {
        var expected = await SearchPathsAsync(query);
        var search = _services.Get<FileSearchService>();
        foreach (var sort in Enum.GetValues<SortField>())
        {
            foreach (var descending in new[] { false, true })
            {
                var paths = new List<string>();
                string? after = null;
                int? total = null;
                do
                {
                    var page = (await search.SearchAsync(query, "", after, 3, sort, descending, TestContext.CancellationToken))!;
                    total ??= page.Total;
                    paths.AddRange(page.Items.Select(i => i.Path));
                    after = page.HasMore ? page.Next : null;
                }
                while (after is not null);

                var label = $"“{query}” by {sort}{(descending ? " descending" : "")}";
                Assert.HasCount(expected.Length, paths, label);
                CollectionAssert.AreEquivalent(expected, paths, label);
                Assert.AreEqual(expected.Length, total, label);
            }
        }
    }

    [TestMethod]
    public async Task TagSearchesAreScopedToTheFolder()
    {
        CollectionAssert.AreEquivalent(new[] { Photo, Holiday, Episode2 }, await SearchPathsAsync("t:animal", "Media"));
        CollectionAssert.AreEquivalent(new[] { RepeatedAFile1 }, await SearchPathsAsync("t:cat", "RepeatedDir"));
        CollectionAssert.AreEquivalent(new[] { RepeatedAFile2 }, await SearchPathsAsync("special:untagged afile", "RepeatedDir/ChildDir"));
        Assert.IsEmpty(await SearchPathsAsync("t:dog", "ChildDir"));
    }

    [TestMethod]
    public async Task ResultsCarryTheTagsSetOnTheFile()
    {
        var items = (await _services.Get<FileSearchService>().SearchAsync("t:animal", "Media", null, 100, cancellationToken: TestContext.CancellationToken))!.Items;

        // The tags set on the file, not the parents they are found through.
        CollectionAssert.AreEquivalent(new[] { "Cat", "Vacation" }, items.Single(i => i.Path == Photo).Tags!.Select(t => t.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Kitten", "Science Fiction" }, items.Single(i => i.Path == Episode2).Tags!.Select(t => t.Name).ToArray());
    }

    [TestMethod]
    public async Task TagEditsChangeWhatSearchesFind()
    {
        var tags = _services.Get<TagService>();
        var vacation = (await tags.SearchAsync("vacation", 1, TestContext.CancellationToken)).Matches.Single();
        var dog = (await tags.SearchAsync("dog", 1, TestContext.CancellationToken)).Matches.Single();

        // Dog moves under Vacation: its files leave "Animal" and join "Vacation".
        Assert.AreEqual(TagEditStatus.Ok, (await tags.UpdateAsync(dog.Id, new TagInput("Dog", ParentIds: [vacation.Id]), TestContext.CancellationToken)).Status);
        CollectionAssert.AreEquivalent(new[] { Photo, RepeatedAFile1, Episode2 }, await SearchPathsAsync("t:animal"));
        CollectionAssert.AreEquivalent(new[] { Photo, Holiday, RFile1 }, await SearchPathsAsync("t:vacation"));

        // Renaming the shorthand: the old one finds nothing any more.
        Assert.AreEqual(TagEditStatus.Ok, (await tags.UpdateAsync(vacation.Id, new TagInput("Vacation", Shorthand: "Trip"), TestContext.CancellationToken)).Status);
        Assert.IsEmpty(await SearchPathsAsync("t:vac"));
        CollectionAssert.AreEquivalent(new[] { Photo, Holiday, RFile1 }, await SearchPathsAsync("t:trip"));

        // Hiding a tag hides the files it is set on from searches that don't ask for it.
        Assert.AreEqual(TagEditStatus.Ok, (await tags.UpdateAsync(vacation.Id, new TagInput("Vacation", IsHidden: true), TestContext.CancellationToken)).Status);
        CollectionAssert.AreEquivalent(new[] { RepeatedAFile1, Episode2 }, await SearchPathsAsync("t:animal"));
        CollectionAssert.AreEquivalent(new[] { Photo, Holiday, RFile1 }, await SearchPathsAsync("t:vacation"));
        // Asking for a child of a hidden tag is not asking for the hidden tag: Holiday is tagged Vacation itself.
        CollectionAssert.AreEquivalent(new[] { RFile1 }, await SearchPathsAsync("t:dog"));
    }

    private async Task AssertFindsAsync(string query, string expected, string path = "")
    {
        var paths = await SearchPathsAsync(query, path);
        CollectionAssert.AreEquivalent(expected.Split('|', StringSplitOptions.RemoveEmptyEntries), paths,
            $"“{query}” found: {string.Join(", ", paths)}");
    }

    private async Task<string[]> SearchPathsAsync(string query, string path = "") =>
        ((await _services.Get<FileSearchService>().SearchAsync(query, path, null, 100, cancellationToken: TestContext.CancellationToken))
            ?? throw new AssertFailedException("Search scope not found.")).Items.Select(i => i.Path).ToArray();

    /// <summary>The fixture's media are not real pictures: their resolutions and durations are written into the index.</summary>
    private async Task SetResolutionsAsync()
    {
        await using var db = await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(TestContext.CancellationToken);
        foreach (var (path, width, height, duration) in new (string, int, int, double?)[]
                 { (Photo, 4032, 3024, null), (Holiday, 1920, 1080, 5400.4), (Episode2, 1280, 720, 1500), (Episode10, 0, 0, 0) })
        {
            var fullName = _library.FullPath(path);
            Assert.AreEqual(1, await db.Files.Where(f => f.FullName == fullName)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.Width, width).SetProperty(f => f.Height, height).SetProperty(f => f.Duration, duration),
                    TestContext.CancellationToken));
        }
    }

    private async Task TagLibraryAsync()
    {
        var tags = _services.Get<TagService>();
        async Task<int> CreateAsync(TagInput input)
        {
            var result = await tags.CreateAsync(input, TestContext.CancellationToken);
            Assert.AreEqual(TagEditStatus.Ok, result.Status);
            return result.Tag!.Id;
        }

        async Task TagAsync(string[] paths, params int[] tagIds)
        {
            var (result, errors) = await tags.ChangeFileTagsAsync(new FileTagChange(paths, Add: tagIds), TestContext.CancellationToken);
            Assert.IsNotNull(result, string.Join("; ", errors?.SelectMany(e => e.Value) ?? []));
        }

        var animal = await CreateAsync(new TagInput("Animal"));
        var cat = await CreateAsync(new TagInput("Cat", ParentIds: [animal]));
        var kitten = await CreateAsync(new TagInput("Kitten", Aliases: ["Kitty"], ParentIds: [cat]));
        var dog = await CreateAsync(new TagInput("Dog", ParentIds: [animal]));
        var vacation = await CreateAsync(new TagInput("Vacation", Shorthand: "Vac", Aliases: ["Holiday trip"]));
        var scienceFiction = await CreateAsync(new TagInput("Science Fiction"));

        await TagAsync([Photo, RepeatedAFile1], cat);
        await TagAsync([Photo, Holiday], vacation);
        await TagAsync([Holiday, RFile1], dog);
        await TagAsync([Episode2], kitten);
        await TagAsync([Episode2, Episode10], scienceFiction);
        await TagAsync([RFile2], BuiltInTags.Archived);
        await TagAsync([ChildAFile1], BuiltInTags.Favorite);
    }
}
