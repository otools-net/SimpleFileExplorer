using System.Text;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Search;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The library's search sends the database the same SQL as before the search was made generic for the files of
/// connections (agents/docs/features/connection-index.md, phase 3): every command of a set of searches (terms,
/// sorts, cursors, a folder, folder rules) compared, as text, with the one committed in tests/samples/search-sql. A
/// change meant to alter the library's SQL writes the file again: <see cref="UpdateCommand"/>.
/// </summary>
[TestClass]
public class SearchSqlTests
{
    /// <summary>
    /// The variable that makes <see cref="WritesTheExpectedSqlWhenAsked"/> write the files: <c>write</c>.
    /// </summary>
    public const string UpdateVariable = "SFE_SEARCH_SQL";

    public const string UpdateCommand =
        $"{UpdateVariable}=write dotnet test tests/Sfe.Tests --filter FullyQualifiedName~SearchSqlTests";

    private static readonly string[] Queries =
    [
        "",
        "episode",
        "episode -10, photo",
        "*media/ep",
        "e:mkv s:>100kb",
        "-e:txt -s:<1kb",
        "w:>=1000 h:<2000 r:1920x1080",
        "-w:1920 r:hr",
        "r:lr",
        "d:>10m -d:<1h",
        "type:video,type:audio",
        "type:other",
        "t:Cat",
        "-t:Cat t:Vacation",
        "special:untagged",
        "t:Archived",
    ];

    public TestContext TestContext { get; set; } = null!;

    protected virtual string Provider => "sqlite";

    [TestMethod]
    public async Task TheLibrarysSearchSendsTheSameSqlAsBefore()
    {
        var expected = await File.ReadAllTextAsync(ExpectedPath, TestContext.CancellationToken);

        var actual = await RunAsync();

        if (actual != expected.ReplaceLineEndings("\n"))
        {
            var at = FirstDifference(expected.ReplaceLineEndings("\n"), actual);
            Assert.Fail(
                $"The library's search sends other SQL than {ExpectedPath} holds, from:\n{Around(actual, at)}\n"
                    + $"When that is meant, write the file again: {UpdateCommand}"
            );
        }
    }

    /// <summary>The documented command: writes the SQL sent now as the expected one.</summary>
    [TestMethod]
    public async Task WritesTheExpectedSqlWhenAsked()
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) != "write")
        {
            Assert.Inconclusive($"Set {UpdateVariable}=write to write the expected SQL.");
            return;
        }

        var actual = await RunAsync();

        Directory.CreateDirectory(Path.GetDirectoryName(ExpectedPath)!);
        await File.WriteAllTextAsync(ExpectedPath, actual, TestContext.CancellationToken);
    }

    protected virtual Task<CoreServices> CreateServicesAsync(TestLibrary library, CommandLog log) =>
        CoreServices.CreateAsync(library, logging: log.AddTo);

    protected virtual Task DropAsync(CoreServices services) => Task.CompletedTask;

    private string ExpectedPath =>
        Path.GetFullPath(Path.Combine(TestSamples.Folder, "..", "search-sql", $"{Provider}.sql"));

    private async Task<string> RunAsync()
    {
        using var library = new TestLibrary();
        var log = new CommandLog();
        await using var services = await CreateServicesAsync(library, log);
        try
        {
            await PrepareAsync(services);
            return await SearchAllAsync(services.Get<FileSearchService>(), log);
        }
        finally
        {
            await DropAsync(services);
        }
    }

    private async Task<string> SearchAllAsync(FileSearchService search, CommandLog log)
    {
        var text = new StringBuilder();
        async Task SearchAsync(string title, Func<Task<SearchResults?>> run)
        {
            log.Clear();
            var results = await run();
            Assert.IsNotNull(results, title);
            text.Append("## ").Append(title).Append('\n');
            foreach (var command in log.Commands)
                text.Append(command).Append("\n;\n");
            text.Append('\n');
        }

        var token = TestContext.CancellationToken;
        foreach (var query in Queries)
            await SearchAsync($"q={query}", () => search.SearchAsync(query, "", null, 100, cancellationToken: token));
        await SearchAsync("in Media", () => search.SearchAsync("e", "Media", null, 100, cancellationToken: token));
        var visible = new PathFilter([
            new PathRegion("Media", []),
            new PathRegion("RepeatedDir", ["RepeatedDir/ChildDir"]),
        ]);
        await SearchAsync("folder rules", () => search.SearchAsync("e", "", null, 100, default, false, token, visible));
        foreach (var sort in Enum.GetValues<SortField>())
        {
            foreach (var descending in new[] { false, true })
            {
                string? after = null;
                for (var page = 1; page <= 3; page++)
                {
                    var asked = after;
                    var title = $"sort={sort} desc={descending} page={page}";
                    SearchResults? results = null;
                    await SearchAsync(title, async () =>
                        results = await search.SearchAsync("", "", asked, 2, sort, descending, token)
                    );
                    after = results!.Next;
                }
            }
        }
        return text.ToString();
    }

    /// <summary>Indexes the library, tags two files and gives two files a resolution and a duration.</summary>
    private async Task PrepareAsync(CoreServices services)
    {
        var token = TestContext.CancellationToken;
        var library = services.Get<Core.Paths.LibraryPaths>().Root;
        await services.Get<DirectoryIndexer>().IndexAsync(library, true, new ScanProgress(), token);
        var tags = services.Get<TagService>();
        var cat = (await tags.CreateAsync(new TagInput("Cat"), token)).Tag!.Id;
        var vacation = (await tags.CreateAsync(new TagInput("Vacation"), token)).Tag!.Id;
        await tags.ChangeFileTagsAsync(new FileTagChange(["Media/photo.jpg"], Add: [cat, vacation]), token);
        await tags.ChangeFileTagsAsync(new FileTagChange(["RFile2"], Add: [BuiltInTags.Archived]), token);

        await using var db = await services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(token);
        await db
            .Files.Where(f => f.Name == "photo")
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Width, 4032).SetProperty(f => f.Height, 3024), token);
        await db
            .Files.Where(f => f.Extension == ".mkv")
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(f => f.Width, 1280)
                        .SetProperty(f => f.Height, 720)
                        .SetProperty(f => f.Duration, 1500),
                token
            );
    }

    private static int FirstDifference(string expected, string actual)
    {
        var length = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < length; i++)
        {
            if (expected[i] != actual[i])
                return i;
        }
        return length;
    }

    private static string Around(string text, int at)
    {
        var start = Math.Max(
            0,
            text.LastIndexOf("## ", Math.Min(at, Math.Max(0, text.Length - 1)), StringComparison.Ordinal)
        );
        return text.Substring(start, Math.Min(1500, text.Length - start));
    }
}
