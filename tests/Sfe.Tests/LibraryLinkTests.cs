using System.Net;
using System.Net.Http.Json;
using Sfe.Api.Endpoints;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Writing through a link (junction, symbolic link) that leaves the library, by <c>Sfe:Security:LinksOutsideLibrary</c>
/// (audit 2026-11, A1). Inconclusive where folder links can't be made.
/// </summary>
[TestClass]
public sealed class LibraryLinkTests
{
    /// <summary>The link in the library, leading to <see cref="_outside"/>.</summary>
    private const string Link = "ChildDir/Out";

    private TestLibrary _library = null!;
    private string _outside = null!;
    private readonly List<IDisposable> _disposables = [];
    private readonly List<string> _links = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _library = new TestLibrary();
        _outside = Path.Combine(_library.BasePath, "outside");
        Directory.CreateDirectory(Path.Combine(_outside, "Sub"));
        File.WriteAllText(Path.Combine(_outside, "theirs.txt"), "not the library's");
        if (!TryLinkFolder(Link, _outside) || !TryLinkFolder("ChildDir/Inside", _library.FullPath("Media")))
            Assert.Inconclusive("Folder links can't be made here.");
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            disposable.Dispose();
        // Not through the links: deleting the library through them would delete what they lead to.
        foreach (var link in _links.Where(Directory.Exists))
            Directory.Delete(link);
        _library.Dispose();
    }

    [TestMethod]
    public async Task ByDefaultLinksLeavingTheLibraryAreWrittenThrough()
    {
        var admin = await AdminAsync();

        Assert.AreEqual(
            HttpStatusCode.OK,
            await PostAsync(admin, "/api/files/folders", new CreateFolderRequest(Link, "made"))
        );
        Assert.IsTrue(Directory.Exists(Path.Combine(_outside, "made")));
    }

    [TestMethod]
    public async Task ByDefaultUploadsAndCopiesGoThroughToo()
    {
        var admin = await AdminAsync();

        var upload = await PostAsync(
            admin,
            "/api/files/uploads",
            new
            {
                target = Link,
                name = "up.txt",
                size = 3,
            }
        );
        using var copy = await admin.PostAsJsonAsync(
            "/api/files/jobs",
            new
            {
                kind = "copy",
                paths = new[] { "RFile1.txt" },
                target = Link,
            },
            Token
        );
        var copied = Path.Combine(_outside, "RFile1.txt");
        for (var waited = 0; waited < 50 && !File.Exists(copied); waited++)
            await Task.Delay(100, Token);

        Assert.IsTrue(upload is HttpStatusCode.OK or HttpStatusCode.Created, upload.ToString());
        Assert.IsTrue(File.Exists(copied), await copy.Content.ReadAsStringAsync(Token));
    }

    [TestMethod]
    public async Task ReadOnlyLinksAreStillReadThrough()
    {
        var admin = await AdminAsync(ReadOnly);

        using var file = await admin.GetAsync($"/api/files/raw/{Link}/theirs.txt", Token);
        using var listing = await admin.GetAsync($"/api/browse?path={Link}", Token);

        Assert.AreEqual(HttpStatusCode.OK, file.StatusCode);
        Assert.AreEqual("not the library's", await file.Content.ReadAsStringAsync(Token));
        Assert.AreEqual(HttpStatusCode.OK, listing.StatusCode);
    }

    [TestMethod]
    public async Task ReadOnlyLinksAreNoWayToCreateRenameOrDeleteOutsideTheLibrary()
    {
        var admin = await AdminAsync(ReadOnly);

        var folder = await PostAsync(admin, "/api/files/folders", new CreateFolderRequest(Link, "made"));
        var deeper = await PostAsync(admin, "/api/files/folders", new CreateFolderRequest($"{Link}/Sub", "made"));
        var file = await PostAsync(admin, "/api/files/new-file", new CreateFileRequest(Link, "made.txt"));
        var rename = await PostAsync(admin, "/api/files/rename", new RenameRequest($"{Link}/theirs.txt", "mine.txt"));
        using var delete = await admin.PostAsJsonAsync(
            "/api/files/delete",
            new DeleteRequest([$"{Link}/theirs.txt"], Permanent: true),
            Token
        );
        var deleted = await delete.Content.ReadAsStringAsync(Token);

        Assert.AreEqual(HttpStatusCode.NotFound, folder);
        Assert.AreEqual(HttpStatusCode.NotFound, deeper);
        Assert.AreEqual(HttpStatusCode.NotFound, file);
        Assert.AreEqual(HttpStatusCode.NotFound, rename);
        StringAssert.Contains(deleted, "notFound", deleted);
        CollectionAssert.AreEquivalent(
            new[] { "Sub", "theirs.txt" },
            Directory.GetFileSystemEntries(_outside).Select(Path.GetFileName).ToArray()
        );
        Assert.IsEmpty(Directory.GetFileSystemEntries(Path.Combine(_outside, "Sub")));
    }

    [TestMethod]
    public async Task ReadOnlyLinksTakeNoUploadAndNoMoveOrCopyInto()
    {
        var admin = await AdminAsync(ReadOnly);

        var upload = await PostAsync(
            admin,
            "/api/files/uploads",
            new
            {
                target = Link,
                name = "up.txt",
                size = 3,
            }
        );
        using var copy = await admin.PostAsJsonAsync(
            "/api/files/jobs",
            new
            {
                kind = "copy",
                paths = new[] { "RFile1.txt" },
                target = Link,
            },
            Token
        );
        // The job runs in the background: what it would have made never appears.
        await Task.Delay(500, Token);

        Assert.AreNotEqual(HttpStatusCode.OK, upload);
        Assert.AreNotEqual(HttpStatusCode.Created, upload);
        Assert.IsFalse(File.Exists(Path.Combine(_outside, "RFile1.txt")), await copy.Content.ReadAsStringAsync(Token));
        Assert.IsFalse(File.Exists(Path.Combine(_outside, "up.txt")));
    }

    [TestMethod]
    public async Task ALinkStayingInsideTheLibraryIsWrittenThroughWhateverTheSetting()
    {
        var admin = await AdminAsync(ReadOnly);

        Assert.AreEqual(
            HttpStatusCode.OK,
            await PostAsync(admin, "/api/files/folders", new CreateFolderRequest("ChildDir/Inside", "made"))
        );
        Assert.IsTrue(Directory.Exists(_library.FullPath("Media/made")));
    }

    [TestMethod]
    public async Task AFolderNamedAsALinkTargetIsWrittenThrough()
    {
        var admin = await AdminAsync(settings =>
        {
            ReadOnly(settings);
            settings["Sfe:Security:LinkTargets:0"] = _outside;
        });

        Assert.AreEqual(
            HttpStatusCode.OK,
            await PostAsync(admin, "/api/files/folders", new CreateFolderRequest($"{Link}/Sub", "made"))
        );
        Assert.IsTrue(Directory.Exists(Path.Combine(_outside, "Sub", "made")));
    }

    private static void ReadOnly(IDictionary<string, string?> settings) =>
        settings["Sfe:Security:LinksOutsideLibrary"] = "ReadOnly";

    private async Task<HttpClient> AdminAsync(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        var admin = await ApiClients.SetUpAsync(factory, Token);
        _disposables.Add(admin);
        return admin;
    }

    private async Task<HttpStatusCode> PostAsync(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body, Token);
        return response.StatusCode;
    }

    /// <summary>
    /// Links <paramref name="link"/> (in the library) to <paramref name="target"/>: a junction on Windows (no rights
    /// needed).
    /// </summary>
    private bool TryLinkFolder(string link, string target)
    {
        var linkPath = _library.FullPath(link);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var mklink = new System.Diagnostics.ProcessStartInfo(
                    "cmd.exe",
                    $"/c mklink /J \"{linkPath}\" \"{target}\""
                )
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                };
                using var process = System.Diagnostics.Process.Start(mklink)!;
                process.WaitForExit();
                if (process.ExitCode != 0)
                    return false;
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, target);
            }

            _links.Add(linkPath);
            return true;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
