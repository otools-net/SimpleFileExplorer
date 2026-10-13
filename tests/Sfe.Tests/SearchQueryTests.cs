using Sfe.Core.Library;
using Sfe.Core.Search;

namespace Sfe.Tests;

[TestClass]
public sealed class SearchQueryTests
{
    [TestMethod]
    public void CommaSeparatesAlternativesAndSpaceSeparatesRequiredTerms()
    {
        var query = SearchQuery.Parse(" cat dog ,  bird ");

        Assert.IsFalse(query.MatchFullPath);
        Assert.HasCount(2, query.Groups);
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, query.Groups[0].Include.ToArray());
        CollectionAssert.AreEqual(new[] { "bird" }, query.Groups[1].Include.ToArray());
    }

    [TestMethod]
    public void DashExcludesTerm()
    {
        var group = SearchQuery.Parse("RDFile -1 -df").Groups.Single();

        CollectionAssert.AreEqual(new[] { "RDFile" }, group.Include.ToArray());
        CollectionAssert.AreEqual(new[] { "1", "df" }, group.Exclude.ToArray());
    }

    [TestMethod]
    public void LeadingStarMatchesFullPath()
    {
        var query = SearchQuery.Parse("*holiday 2020");

        Assert.IsTrue(query.MatchFullPath);
        CollectionAssert.AreEqual(new[] { "holiday", "2020" }, query.Groups.Single().Include.ToArray());
    }

    [TestMethod]
    [DataRow("s:<1mb", SizeComparison.LessThan, 1024L * 1024, 0L)]
    [DataRow("s:<=10kb", SizeComparison.LessThanOrEqual, 10L * 1024, 0L)]
    [DataRow("s:>1.5GB", SizeComparison.GreaterThan, 1536L * 1024 * 1024, 0L)]
    [DataRow("S:>=2tb", SizeComparison.GreaterThanOrEqual, 2L * 1024 * 1024 * 1024 * 1024, 0L)]
    [DataRow("s:=0b", SizeComparison.Equal, 0L, 0L)]
    [DataRow("s:700", SizeComparison.Approximately, 700L * 1024 * 1024, 512L * 1024)]
    public void ParsesSizeFilters(string token, SizeComparison comparison, long bytes, long tolerance)
    {
        var size = SearchQuery.Parse(token).Groups.Single().Sizes.Single();

        Assert.AreEqual(new SizeFilter(comparison, bytes, tolerance), size);
    }

    [TestMethod]
    [DataRow("w:>=1920", Dimension.Width, SizeComparison.GreaterThanOrEqual, 1920)]
    [DataRow("W:<720", Dimension.Width, SizeComparison.LessThan, 720)]
    [DataRow("h:1080", Dimension.Height, SizeComparison.Equal, 1080)]
    [DataRow("h:=480", Dimension.Height, SizeComparison.Equal, 480)]
    [DataRow("h:<=4000", Dimension.Height, SizeComparison.LessThanOrEqual, 4000)]
    public void ParsesWidthAndHeightFilters(string token, Dimension dimension, SizeComparison comparison, int pixels)
    {
        var filter = SearchQuery.Parse(token).Groups.Single().Dimensions.Single();

        Assert.AreEqual(new DimensionFilter(dimension, comparison, pixels), filter);
    }

    [TestMethod]
    public void ResolutionFiltersCompareBothSidesAlike()
    {
        var group = SearchQuery.Parse("r:>720X1024").Groups.Single();

        CollectionAssert.AreEqual(
            new[] { new DimensionFilter(Dimension.Width, SizeComparison.GreaterThan, 720), new DimensionFilter(Dimension.Height, SizeComparison.GreaterThan, 1024) },
            group.Dimensions.ToArray());
    }

    [TestMethod]
    [DataRow("d:>10m", SizeComparison.GreaterThan, 600d, 0d)]
    [DataRow("d:<=30s", SizeComparison.LessThanOrEqual, 30d, 0d)]
    [DataRow("D:>=1.5h", SizeComparison.GreaterThanOrEqual, 5400d, 0d)]
    [DataRow("d:<90", SizeComparison.LessThan, 5400d, 0d)]
    [DataRow("d:10", SizeComparison.Approximately, 600d, 30d)]
    [DataRow("d:1h", SizeComparison.Approximately, 3600d, 1800d)]
    [DataRow("d:45s", SizeComparison.Approximately, 45d, 0.5d)]
    [DataRow("d:1:30", SizeComparison.Approximately, 90d, 0.5d)]
    [DataRow("d:>1:30:00", SizeComparison.GreaterThan, 5400d, 0d)]
    [DataRow("d:=0:05", SizeComparison.Equal, 5d, 0d)]
    public void ParsesDurationFilters(string token, SizeComparison comparison, double seconds, double tolerance)
    {
        var duration = SearchQuery.Parse(token).Groups.Single().Durations.Single();

        Assert.AreEqual(new DurationFilter(comparison, seconds, tolerance), duration);
    }

    [TestMethod]
    [DataRow("d:long")]
    [DataRow("d:")]
    [DataRow("d:10min")]
    [DataRow("d:1:2:3:4")]
    [DataRow("d:>:30")]
    [DataRow("-d:x")]
    public void IgnoresMalformedDurationFilters(string token)
    {
        var group = SearchQuery.Parse("movie " + token).Groups.Single();

        AssertGroup(group, include: ["movie"], excludedFilters: 0);
        Assert.IsEmpty(group.Durations);
    }

    [TestMethod]
    public void ParsesHighAndLowResolution()
    {
        var group = SearchQuery.Parse("r:hr R:LR -r:Hr").Groups.Single();

        CollectionAssert.AreEqual(new[] { ResolutionLevel.High, ResolutionLevel.Low }, group.Levels.ToArray());
        Assert.IsEmpty(group.Dimensions);
        CollectionAssert.AreEqual(new[] { ResolutionLevel.High }, group.ExcludedFilters.Single().Levels.ToArray());
        // Anything else after r: is a width x height, ignored when malformed.
        AssertGroup(SearchQuery.Parse("movie r:mr").Groups.Single(), include: ["movie"], excludedFilters: 0);
    }

    [TestMethod]
    public void ParsesTypeFilters()
    {
        var group = SearchQuery.Parse("type:image TYPE:Video type:audio type:other -type:video").Groups.Single();

        CollectionAssert.AreEqual(new[] { ItemKind.Image, ItemKind.Video, ItemKind.Audio, ItemKind.File }, group.Kinds.ToArray());
        CollectionAssert.AreEqual(new[] { ItemKind.Video }, group.ExcludedFilters.Single().Kinds.ToArray());
        // "type:" is not "t:": no tag term.
        Assert.IsEmpty(group.Tags);
    }

    [TestMethod]
    [DataRow("type:")]
    [DataRow("type:folder")]
    [DataRow("type:images")]
    [DataRow("-type:picture")]
    public void IgnoresMalformedTypeFilters(string token)
    {
        var group = SearchQuery.Parse("movie " + token).Groups.Single();

        AssertGroup(group, include: ["movie"], excludedFilters: 0);
        Assert.IsEmpty(group.Kinds);
    }

    [TestMethod]
    [DataRow("w:big")]
    [DataRow("w:")]
    [DataRow("h:>1.5")]
    [DataRow("r:1920")]
    [DataRow("r:1920x")]
    [DataRow("w:99999999999")]
    public void IgnoresMalformedResolutionFilters(string token)
    {
        var group = SearchQuery.Parse("movie " + token).Groups.Single();

        CollectionAssert.AreEqual(new[] { "movie" }, group.Include.ToArray());
        Assert.IsEmpty(group.Dimensions);
    }

    [TestMethod]
    public void NegatedFiltersAreExcludedEachAsAWhole()
    {
        var group = SearchQuery.Parse("movie -w:1920 -r:>720x1024 -s:<1mb -e:mp4 -e:.mkv").Groups.Single();

        AssertGroup(group, include: ["movie"]);
        Assert.IsEmpty(group.Dimensions);
        var excluded = group.ExcludedFilters;
        Assert.HasCount(5, excluded);
        CollectionAssert.AreEqual(new[] { new DimensionFilter(Dimension.Width, SizeComparison.Equal, 1920) }, excluded[0].Dimensions.ToArray());
        CollectionAssert.AreEqual(
            new[] { new DimensionFilter(Dimension.Width, SizeComparison.GreaterThan, 720), new DimensionFilter(Dimension.Height, SizeComparison.GreaterThan, 1024) },
            excluded[1].Dimensions.ToArray());
        CollectionAssert.AreEqual(new[] { new SizeFilter(SizeComparison.LessThan, 1024L * 1024) }, excluded[2].Sizes.ToArray());
        CollectionAssert.AreEqual(new[] { ".mp4" }, excluded[3].Extensions.ToArray());
        CollectionAssert.AreEqual(new[] { ".mkv" }, excluded[4].Extensions.ToArray());
    }

    [TestMethod]
    [DataRow("-s:big")]
    [DataRow("-w:")]
    [DataRow("-r:1920")]
    [DataRow("-e:")]
    [DataRow("-E:.")]
    public void MalformedNegatedFiltersAreIgnored(string token)
    {
        var group = SearchQuery.Parse("movie " + token).Groups.Single();

        AssertGroup(group, include: ["movie"]);
        Assert.IsEmpty(group.ExcludedFilters);
    }

    [TestMethod]
    public void IgnoresMalformedSizeFilters()
    {
        var query = SearchQuery.Parse("movie s:big");

        var group = query.Groups.Single();
        CollectionAssert.AreEqual(new[] { "movie" }, group.Include.ToArray());
        Assert.IsEmpty(group.Sizes);
    }

    [TestMethod]
    public void ParsesExtensionFilter()
    {
        var group = SearchQuery.Parse("e:mp4 e:.MKV").Groups.Single();

        CollectionAssert.AreEqual(new[] { ".mp4", ".MKV" }, group.Extensions.ToArray());
    }

    [TestMethod]
    public void EmptyQueryHasNoGroups()
    {
        Assert.IsTrue(SearchQuery.Parse("  , ,").IsEmpty);
        Assert.IsTrue(SearchQuery.Parse(null).IsEmpty);
    }

    [TestMethod]
    [DataRow(@"V:\", @"V:\\", @"V:\\%")]
    [DataRow(@"V:\Movies", @"V:\\Movies", @"V:\\Movies\\%")]
    [DataRow(@"V:\Movies\", @"V:\\Movies", @"V:\\Movies\\%")]
    public void TreePatternsHandleDriveRoots(string directory, string self, string descendants)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows paths.");

        Assert.AreEqual((self, descendants), SearchPredicateBuilder.TreePatterns(directory));
    }

    [TestMethod]
    [DataRow("/", "/", "/%")]
    [DataRow("/srv/media", "/srv/media", "/srv/media/%")]
    [DataRow("/srv/media/", "/srv/media", "/srv/media/%")]
    public void TreePatternsHandleUnixRoots(string directory, string self, string descendants)
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Unix paths.");

        Assert.AreEqual((self, descendants), SearchPredicateBuilder.TreePatterns(directory));
    }

    [TestMethod]
    public void EscapesLikeWildcards()
    {
        Assert.AreEqual(@"100\%\_done\[1]\\x", SearchPredicateBuilder.EscapeLike(@"100%_done[1]\x"));
    }

    [TestMethod]
    public void ParsesTagTerms()
    {
        var group = SearchQuery.Parse("holiday t:beach -tag:Work T:\"Science Fiction\" special:untagged").Groups.Single();

        CollectionAssert.AreEqual(new[] { "holiday" }, group.Include.ToArray());
        CollectionAssert.AreEqual(
            new[] { new TagFilter("beach"), new TagFilter("Work", Negated: true), new TagFilter("Science Fiction"), new TagFilter(null, Negated: true) },
            group.Tags.ToArray());
        Assert.AreEqual(new TagFilter(null), SearchQuery.Parse("-special:untagged").Groups.Single().Tags.Single());
    }

    [TestMethod]
    public void QuotesKeepSeparatorsInsideATerm()
    {
        var query = SearchQuery.Parse("\"cats, dogs\" t:\"a, b\", bird");

        Assert.HasCount(2, query.Groups);
        CollectionAssert.AreEqual(new[] { "cats, dogs" }, query.Groups[0].Include.ToArray());
        Assert.AreEqual("a, b", query.Groups[0].Tags.Single().Name);
        CollectionAssert.AreEqual(new[] { "bird" }, query.Groups[1].Include.ToArray());
    }

    [TestMethod]
    public void EmptyTagTermIsAPlainWord()
    {
        CollectionAssert.AreEqual(new[] { "t:" }, SearchQuery.Parse("t:").Groups.Single().Include.ToArray());
        CollectionAssert.AreEqual(new[] { "t:" }, SearchQuery.Parse("-t:").Groups.Single().Exclude.ToArray());
    }

    [TestMethod]
    public void ParsesEveryKindOfTermInOneGroup()
    {
        var group = SearchQuery.Parse("holiday -draft s:>1mb s:<2gb e:mp4 e:mkv t:beach -tag:work -special:untagged").Groups.Single();

        CollectionAssert.AreEqual(new[] { "holiday" }, group.Include.ToArray());
        CollectionAssert.AreEqual(new[] { "draft" }, group.Exclude.ToArray());
        CollectionAssert.AreEqual(
            new[] { new SizeFilter(SizeComparison.GreaterThan, 1024L * 1024), new SizeFilter(SizeComparison.LessThan, 2L * 1024 * 1024 * 1024) },
            group.Sizes.ToArray());
        CollectionAssert.AreEqual(new[] { ".mp4", ".mkv" }, group.Extensions.ToArray());
        CollectionAssert.AreEqual(new[] { new TagFilter("beach"), new TagFilter("work", Negated: true), new TagFilter(null) }, group.Tags.ToArray());
    }

    [TestMethod]
    public void ParsesEveryKindOfNegatedTermInOneGroup()
    {
        var query = SearchQuery.Parse("-holiday -s:>1gb -e:mp4 -w:<100 -h:=50 -r:1x1 -t:\"two words\" -special:untagged");

        Assert.IsFalse(query.IsEmpty);
        var group = query.Groups.Single();
        AssertGroup(group, exclude: ["holiday"], tags: [new TagFilter("two words", Negated: true), new TagFilter(null)], excludedFilters: 5);
        Assert.IsEmpty(group.Dimensions);
        Assert.AreEqual(new SizeFilter(SizeComparison.GreaterThan, 1024L * 1024 * 1024), group.ExcludedFilters[0].Sizes.Single());
        CollectionAssert.AreEqual(new[] { ".mp4" }, group.ExcludedFilters[1].Extensions.ToArray());
        Assert.AreEqual(new DimensionFilter(Dimension.Width, SizeComparison.LessThan, 100), group.ExcludedFilters[2].Dimensions.Single());
        Assert.AreEqual(new DimensionFilter(Dimension.Height, SizeComparison.Equal, 50), group.ExcludedFilters[3].Dimensions.Single());
        Assert.HasCount(2, group.ExcludedFilters[4].Dimensions);
    }

    [TestMethod]
    public void AnExcludedFilterHoldsOnlyItsOwnTerm()
    {
        foreach (var excluded in SearchQuery.Parse("-s:1mb -e:mkv -w:10 -d:>5m -r:lr -type:image").Groups.Single().ExcludedFilters)
        {
            Assert.AreEqual(1, excluded.Sizes.Count + excluded.Extensions.Count + excluded.Dimensions.Count + excluded.Durations.Count + excluded.Levels.Count
                + excluded.Kinds.Count);
            AssertGroup(excluded, sizes: [.. excluded.Sizes], extensions: [.. excluded.Extensions], excludedFilters: 0);
        }
    }

    [TestMethod]
    public void NegationsBelongToTheirAlternative()
    {
        var query = SearchQuery.Parse("-e:mp4 cat, e:mp4 -cat, -t:dog,t:dog");

        Assert.HasCount(4, query.Groups);
        AssertGroup(query.Groups[0], include: ["cat"], excludedFilters: 1);
        AssertGroup(query.Groups[1], exclude: ["cat"], extensions: [".mp4"], excludedFilters: 0);
        AssertGroup(query.Groups[2], tags: [new TagFilter("dog", Negated: true)], excludedFilters: 0);
        AssertGroup(query.Groups[3], tags: [new TagFilter("dog")], excludedFilters: 0);
    }

    [TestMethod]
    public void PositiveAndNegatedFiltersOfOneKindStayApart()
    {
        var group = SearchQuery.Parse("e:mp4 -e:mkv e:avi s:>1mb -s:>1gb w:>100 -w:>1000").Groups.Single();

        AssertGroup(group, sizes: [new SizeFilter(SizeComparison.GreaterThan, 1024L * 1024)], extensions: [".mp4", ".avi"], excludedFilters: 3);
        CollectionAssert.AreEqual(new[] { new DimensionFilter(Dimension.Width, SizeComparison.GreaterThan, 100) }, group.Dimensions.ToArray());
    }

    [TestMethod]
    [DataRow("--draft", "-draft")]
    [DataRow("--e:mp4", "-e:mp4")]
    [DataRow("--t:cat", "-t:cat")]
    [DataRow("-\"-x\"", "-x")]
    public void ASecondDashIsPartOfTheExcludedWord(string token, string excluded)
    {
        AssertGroup(SearchQuery.Parse(token).Groups.Single(), exclude: [excluded], excludedFilters: 0);
    }

    [TestMethod]
    public void ALoneDashIsIgnored()
    {
        AssertGroup(SearchQuery.Parse("movie - -\"\"").Groups.Single(), include: ["movie"], excludedFilters: 0);
        Assert.IsTrue(SearchQuery.Parse("-").IsEmpty);
        Assert.IsTrue(SearchQuery.Parse(" - , -").IsEmpty);
    }

    [TestMethod]
    public void AFullPathSearchDropsEveryStar()
    {
        var query = SearchQuery.Parse("*a*b, -*c e:*mp4");

        Assert.IsTrue(query.MatchFullPath);
        AssertGroup(query.Groups[0], include: ["ab"]);
        AssertGroup(query.Groups[1], exclude: ["c"], extensions: [".mp4"]);
    }

    [TestMethod]
    public void ExtraSpacesAndCommasChangeNothing()
    {
        var query = SearchQuery.Parse("  cat    -dog   e:jpg ,,,  ,  t:bird   ");

        Assert.HasCount(2, query.Groups);
        AssertGroup(query.Groups[0], include: ["cat"], exclude: ["dog"], extensions: [".jpg"]);
        AssertGroup(query.Groups[1], tags: [new TagFilter("bird")]);
    }

    [TestMethod]
    public void EachAlternativeKeepsItsOwnTerms()
    {
        var query = SearchQuery.Parse("cat -kitten e:jpg, t:dog s:<1mb, -t:cat special:untagged");

        Assert.HasCount(3, query.Groups);
        AssertGroup(query.Groups[0], include: ["cat"], exclude: ["kitten"], extensions: [".jpg"]);
        AssertGroup(query.Groups[1], sizes: [new SizeFilter(SizeComparison.LessThan, 1024L * 1024)], tags: [new TagFilter("dog")]);
        AssertGroup(query.Groups[2], tags: [new TagFilter("cat", Negated: true), new TagFilter(null, Negated: true)]);
        CollectionAssert.AreEquivalent(new[] { "dog", "cat" }, query.TagNames.ToArray());
    }

    [TestMethod]
    public void CommaInsideASizeStartsAnotherAlternative()
    {
        // ',' is never a decimal separator: "s:1,5mb" is "s:1" (about 1 MB) or the word "5mb".
        var query = SearchQuery.Parse("movie s:1,5mb");

        AssertGroup(query.Groups[0], include: ["movie"], sizes: [new SizeFilter(SizeComparison.Approximately, 1024L * 1024, 512L * 1024)]);
        AssertGroup(query.Groups[1], include: ["5mb"]);
    }

    [TestMethod]
    public void QuotedExclusionKeepsItsSpaces()
    {
        var group = SearchQuery.Parse("movie -\"director's cut\" -\"\"").Groups.Single();

        CollectionAssert.AreEqual(new[] { "movie" }, group.Include.ToArray());
        CollectionAssert.AreEqual(new[] { "director's cut" }, group.Exclude.ToArray());
    }

    [TestMethod]
    public void OnlyALeadingStarSwitchesToFullPaths()
    {
        var leading = SearchQuery.Parse("*movies, *series 2020");
        var inner = SearchQuery.Parse("a*b");

        Assert.IsTrue(leading.MatchFullPath);
        CollectionAssert.AreEqual(new[] { "movies" }, leading.Groups[0].Include.ToArray());
        CollectionAssert.AreEqual(new[] { "series", "2020" }, leading.Groups[1].Include.ToArray());
        Assert.IsFalse(inner.MatchFullPath);
        CollectionAssert.AreEqual(new[] { "a*b" }, inner.Groups.Single().Include.ToArray());
    }

    [TestMethod]
    [DataRow("s:")]
    [DataRow("s:10xb")]
    [DataRow("s:-1mb")]
    [DataRow("s:>")]
    [DataRow("s:<>1mb")]
    [DataRow("s:1.5.2mb")]
    public void IgnoresInvalidSizes(string token)
    {
        AssertGroup(SearchQuery.Parse($"movie {token}").Groups.Single(), include: ["movie"]);
    }

    [TestMethod]
    [DataRow("s:1b", SizeComparison.Approximately, 1L, 0L)]
    [DataRow("s:2kb", SizeComparison.Approximately, 2048L, 512L)]
    [DataRow("s:0.5gb", SizeComparison.Approximately, 512L * 1024 * 1024, 512L * 1024 * 1024)]
    [DataRow("s:>0", SizeComparison.GreaterThan, 0L, 0L)]
    [DataRow("S:=1TB", SizeComparison.Equal, 1024L * 1024 * 1024 * 1024, 0L)]
    public void ApproximateSizesAllowHalfAUnit(string token, SizeComparison comparison, long bytes, long tolerance)
    {
        Assert.AreEqual(new SizeFilter(comparison, bytes, tolerance), SearchQuery.Parse(token).Groups.Single().Sizes.Single());
    }

    [TestMethod]
    public void EmptyExtensionIsIgnoredAndNegatedExtensionIsAFilter()
    {
        var group = SearchQuery.Parse("clip e: e:. -e:mp4").Groups.Single();

        Assert.IsEmpty(group.Extensions);
        CollectionAssert.AreEqual(new[] { "clip" }, group.Include.ToArray());
        Assert.IsEmpty(group.Exclude);
        CollectionAssert.AreEqual(new[] { ".mp4" }, group.ExcludedFilters.Single().Extensions.ToArray());
    }

    [TestMethod]
    [DataRow("TAG:Beach", "Beach", false)]
    [DataRow("-T:beach", "beach", true)]
    [DataRow("t:\"  spaced  \"", "spaced", false)]
    [DataRow("t:two_words", "two_words", false)]
    [DataRow("SPECIAL:UNTAGGED", null, true)]
    [DataRow("-Special:Untagged", null, false)]
    public void TagPrefixesIgnoreCase(string token, string? name, bool negated)
    {
        Assert.AreEqual(new TagFilter(name, negated), SearchQuery.Parse(token).Groups.Single().Tags.Single());
    }

    /// <summary>Compares every list of a group (the record itself compares its lists by reference).</summary>
    private static void AssertGroup(
        SearchGroup group, string[]? include = null, string[]? exclude = null, SizeFilter[]? sizes = null, string[]? extensions = null, TagFilter[]? tags = null,
        int? excludedFilters = null)
    {
        if (excludedFilters is not null)
            Assert.HasCount(excludedFilters.Value, group.ExcludedFilters, "ExcludedFilters");
        CollectionAssert.AreEqual(include ?? [], group.Include.ToArray(), "Include");
        CollectionAssert.AreEqual(exclude ?? [], group.Exclude.ToArray(), "Exclude");
        CollectionAssert.AreEqual(sizes ?? [], group.Sizes.ToArray(), "Sizes");
        CollectionAssert.AreEqual(extensions ?? [], group.Extensions.ToArray(), "Extensions");
        CollectionAssert.AreEqual(tags ?? [], group.Tags.ToArray(), "Tags");
    }
}
