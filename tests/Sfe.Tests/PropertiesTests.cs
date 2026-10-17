using System.Security.Cryptography;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Properties of files and folders in Core (<see cref="Properties"/>, <see cref="PropertyJobs"/>).</summary>
[TestClass]
public sealed class PropertiesTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly List<string> _links = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private Properties Properties => _services.Get<Properties>();
    private PropertyJobs Jobs => _services.Get<PropertyJobs>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        foreach (var link in _links)
            Directory.Delete(link);
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public void AFileAndAFolderAreDescribedFromTheDisk()
    {
        var modified = new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_library.FullPath("Media/photo.jpg"), modified);
        _library.AddFile("ChildDir/.hidden", 10);

        var file = Properties.Describe("Media/photo.jpg", _library.FullPath("Media/photo.jpg"))!;
        var folder = Properties.Describe("ChildDir", _library.FullPath("ChildDir"))!;
        var hidden = Properties.Describe("ChildDir/.hidden", _library.FullPath("ChildDir/.hidden"))!;
        var root = Properties.Describe("", _library.Root)!;

        Assert.AreEqual(3 * 1024, file.Size);
        Assert.AreEqual(modified, file.Modified.UtcDateTime);
        Assert.IsFalse(file.IsFolder);
        Assert.IsFalse(file.Hidden);
        Assert.IsTrue(folder.IsFolder);
        Assert.IsNull(folder.Size);
        Assert.IsTrue(hidden.Hidden, "A dot name is hidden");
        Assert.AreEqual("Root", root.Name);
        Assert.IsNull(Properties.Describe("Nothing", _library.FullPath("Nothing")));
    }

    [TestMethod]
    public async Task AFoldersTotalsComeFromTheIndex()
    {
        var before = await Properties.IndexedTotalsAsync(_library.FullPath("RepeatedDir"), null, Token);
        var scans = _services.Get<ScanCoordinator>();
        Assert.IsTrue(scans.TryStart(""));
        await scans.Completion!;

        var totals = await Properties.IndexedTotalsAsync(_library.FullPath("RepeatedDir"), null, Token);

        Assert.AreEqual(new FolderTotals(false, 0, 0, 0), before);
        Assert.AreEqual(new FolderTotals(true, 6, 2, 60), totals);
    }

    [TestMethod]
    public async Task AFolderIsCountedOnTheDiskWithoutHiddenFoldersLinksOrTheWorkFolder()
    {
        _library.AddFile(".sfe/trash/1/deleted.txt", 10);
        var outside = Path.Combine(_library.BasePath, "Outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "big.bin"), new string('x', 5000));
        var linked = Link("RepeatedDir/Linked", outside);
        var access = new FakeAccess { Hidden = path => path == "RepeatedDir/ChildDir" };

        var job = await DoneAsync(Jobs.StartCount("me", "RepeatedDir", _library.FullPath("RepeatedDir"), access));
        var root = await DoneAsync(Jobs.StartCount("me", "", _library.Root, new FakeAccess()));

        Assert.IsNull(job.Error);
        Assert.AreEqual(4, job.Files, "RDFile1, RDFile2 and the two of RepeatedDir/RepeatedDir");
        Assert.AreEqual(1, job.Folders);
        Assert.AreEqual(40, job.Bytes);
        Assert.AreEqual(linked ? 1 : 0, job.LeftOut, "The link, not followed");
        Assert.IsFalse(job.Capped);
        Assert.AreEqual(21, root.Files, "Everything but the work folder");
    }

    [TestMethod]
    public async Task ChecksumsAreThoseOfTheWholeFile()
    {
        var bytes = await File.ReadAllBytesAsync(_library.FullPath("Media/Episode 2.mkv"), Token);

        foreach (var (algorithm, expected) in new[]
        {
            (ChecksumAlgorithm.Sha256, SHA256.HashData(bytes)),
            (ChecksumAlgorithm.Sha1, SHA1.HashData(bytes)),
            (ChecksumAlgorithm.Md5, MD5.HashData(bytes)),
        })
        {
            var job = await DoneAsync(Jobs.StartChecksum("me", "Media/Episode 2.mkv", _library.FullPath("Media/Episode 2.mkv"), algorithm));
            Assert.AreEqual(Convert.ToHexStringLower(expected), job.Checksum, algorithm.ToString());
            Assert.AreEqual(bytes.Length, job.Bytes);
            Assert.AreEqual(bytes.Length, job.Total);
        }
    }

    [TestMethod]
    public async Task JobsAreTheirOwnersOnlyAndANewOneStopsThePrevious()
    {
        var first = Jobs.StartChecksum("me", "Media/Holiday 2020.mp4", _library.FullPath("Media/Holiday 2020.mp4"), ChecksumAlgorithm.Sha256);
        var count = Jobs.StartCount("me", "ChildDir", _library.FullPath("ChildDir"), new FakeAccess());

        Assert.IsNull(Jobs.Get("someone else", first.Id));
        Assert.IsFalse(Jobs.Cancel("someone else", first.Id));
        var second = Jobs.StartChecksum("me", "RFile1.txt", _library.FullPath("RFile1.txt"), ChecksumAlgorithm.Md5);

        Assert.IsNull(Jobs.Get("me", first.Id), "Replaced by the next checksum");
        Assert.IsNotNull(Jobs.Get("me", count.Id), "Another kind stays");
        Assert.IsNotNull((await DoneAsync(second)).Checksum);
    }

    [TestMethod]
    public async Task AJobCanBeStopped()
    {
        var job = Jobs.StartChecksum("me", "Media/Holiday 2020.mp4", _library.FullPath("Media/Holiday 2020.mp4"), ChecksumAlgorithm.Sha256);

        Assert.IsTrue(Jobs.Cancel("me", job.Id));
        var done = await DoneAsync(job);

        Assert.IsTrue(done.Checksum is null && done.Error == "Stopped." || done.Checksum is not null, "Stopped, unless it was already done");
    }

    private async Task<PropertyJobView> DoneAsync(PropertyJobView job)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var now = Jobs.Get("me", job.Id)!;
            if (now.Done)
                return now;
            await Task.Delay(25, Token);
        }
        throw new AssertFailedException("The job did not end in time.");
    }

    private bool Link(string relative, string target)
    {
        var link = _library.FullPath(relative);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                })!;
                process.WaitForExit();
                if (process.ExitCode != 0)
                    return false;
            }
            else
            {
                Directory.CreateSymbolicLink(link, target);
            }
            _links.Add(link);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed class FakeAccess : IPropertyAccess
    {
        public Func<string, bool> Hidden { get; init; } = _ => false;

        public bool Sees(string path) => !Hidden(path);

        public bool IsProtectedReal(string realPath) => realPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal);
    }
}
