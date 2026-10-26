using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Archives extracted as background jobs (<see cref="FileJobs"/>, <see cref="FileJobKind.Extract"/>).</summary>
[TestClass]
public sealed class ExtractJobTests
{
    private static readonly FileActor Me = new(7, "tester");

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly WarningLog _log = new();

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private FileJobs Jobs => _services.Get<FileJobs>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, logging: _log.AddTo);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AnArchiveGoesIntoANewFolderNamedAfterItAndIsIndexed()
    {
        Archive("Media/holiday.zip", ("readme.txt", "hello"), ("Photos/a.txt", "a"));

        var job = await RunAsync(Extract("Media/holiday.zip", "Media"));
        await ScansAsync();

        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("Media/holiday", result.NewPath);
        StringAssert.Contains(result.Message, "2 files (6 B).");
        Assert.AreEqual("hello", File.ReadAllText(_library.FullPath("Media/holiday/readme.txt")));
        Assert.AreEqual("a", File.ReadAllText(_library.FullPath("Media/holiday/Photos/a.txt")));
        Assert.AreEqual(new FileInfo(_library.FullPath("Media/holiday.zip")).Length, job.BytesTotal);
        Assert.IsEmpty(Directory.GetDirectories(_library.FullPath("Media"), "*" + FileJobs.PartialSuffix));
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("Media/holiday/Photos/a.txt"), Token), "a scan of the new folder indexes it");
    }

    [TestMethod]
    public async Task OneTopFolderIsNotNestedTwiceAndATakenNameIsKept()
    {
        Archive("Media/trip.zip", ("trip/day1.txt", "1"), ("trip/day2.txt", "2"));
        Directory.CreateDirectory(_library.FullPath("Media/trip"));

        var job = await RunAsync(Extract("Media/trip.zip", "Media"));

        Assert.AreEqual("Media/trip (2)", job.Results.Single().NewPath, job.Results.Single().Message);
        Assert.IsTrue(File.Exists(_library.FullPath("Media/trip (2)/day1.txt")), "trip/ inside gives trip (2), not trip (2)/trip");
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(_library.FullPath("Media/trip")), "what was there is untouched");
    }

    [TestMethod]
    public async Task ExtractedHereANameClashIsAskedAndTheRestPlaced()
    {
        Archive("Media/here.zip", ("photo.jpg", "from the archive"), ("new.txt", "new"));

        var view = Jobs.Start(Extract("Media/here.zip", "Media") with { NewFolder = false }, Me, new FakeAccess());
        var waiting = await WaitForAsync(view.Id, j => j.State == FileJobState.Waiting);

        Assert.AreEqual("photo.jpg", waiting.Conflict!.Existing.Name);
        Assert.IsTrue(Jobs.Answer(view.Id, ConflictChoice.KeepBoth, forAll: false));
        await Jobs.Completion(view.Id)!;

        var result = Jobs.Find(view.Id)!.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("from the archive", File.ReadAllText(_library.FullPath("Media/photo (2).jpg")));
        Assert.AreEqual("new", File.ReadAllText(_library.FullPath("Media/new.txt")));
        Assert.AreEqual(3 * 1024, new FileInfo(_library.FullPath("Media/photo.jpg")).Length, "the file there is kept");
        Assert.IsEmpty(Directory.GetDirectories(_library.FullPath("Media"), "*" + FileJobs.PartialSuffix));
    }

    [TestMethod]
    public async Task ExtractedHereOverTheFileItWasMadeFromReplacesIt()
    {
        File.WriteAllText(_library.FullPath("README.md"), "old");
        Archive("README.zip", ("README.md", "new"));

        var job = await RunAsync(Extract("README.zip", "") with { NewFolder = false, Conflicts = ConflictChoice.Replace });

        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("new", File.ReadAllText(_library.FullPath("README.md")));
    }

    [TestMethod]
    public async Task NothingIsPlacedIntoTheWorkFolderNorReplacesIt()
    {
        var trash = Directory.CreateDirectory(_library.FullPath(".sfe/trash")).FullName;
        File.WriteAllText(Path.Combine(trash, "kept.txt"), "kept");
        var shortName = ShortName(_library.FullPath(".sfe"));
        var entries = new List<(string, string?)> { (".sfe/trash/planted.txt", "planted"), (".SFE/x.txt", "x"), ("ok.txt", "ok") };
        if (shortName is not null)
            entries.Add(($"{shortName}/trash/short.txt", "short"));
        Archive("drop.zip", [.. entries]);

        var job = await RunAsync(Extract("drop.zip", "") with { NewFolder = false, Conflicts = ConflictChoice.Replace });

        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        StringAssert.Contains(result.Message, "not placed");
        Assert.AreEqual("ok", File.ReadAllText(_library.FullPath("ok.txt")));
        CollectionAssert.AreEquivalent(new[] { "kept.txt" }, Directory.GetFiles(trash).Select(Path.GetFileName).ToArray(), "the trash is untouched");
        Assert.IsFalse(File.Exists(_library.FullPath(".sfe/x.txt")));

        // In a new folder: one top folder named as the work folder takes another name.
        Archive("one.zip", (".sfe/a.txt", "a"));
        var inFolder = (await RunAsync(Extract("one.zip", ""))).Results.Single();
        Assert.AreEqual(".sfe (2)", inFolder.NewPath, inFolder.Message);
        Assert.IsFalse(File.Exists(_library.FullPath(".sfe/a.txt")));
    }

    /// <summary>The 8.3 name of <paramref name="path"/>'s last part (Windows, where the disk makes them), else null.</summary>
    private static string? ShortName(string path)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var buffer = new System.Text.StringBuilder(1024);
        var length = GetShortPathName(path, buffer, buffer.Capacity);
        var name = length > 0 ? Path.GetFileName(buffer.ToString()) : null;
        return name is null || string.Equals(name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern int GetShortPathName(string path, System.Text.StringBuilder shortPath, int length);

    [TestMethod]
    public async Task TheEntriesChosenComeOutAlone()
    {
        Archive("Media/pick.zip", ("top.txt", "top"), ("Photos/2024/a.txt", "a"), ("Photos/2024/b.txt", "b"));

        var job = await RunAsync(Extract("Media/pick.zip", "ChildDir") with { NewFolder = false, Entries = ["Photos/2024/a.txt"] });

        Assert.AreEqual(FileOperationStatus.Done, job.Results.Single().Status, job.Results.Single().Message);
        Assert.AreEqual("a", File.ReadAllText(_library.FullPath("ChildDir/a.txt")));
        Assert.IsFalse(File.Exists(_library.FullPath("ChildDir/top.txt")));
        await using var db = await DbAsync();
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("ChildDir/a.txt"), Token), "an extracted file is indexed at once");
    }

    [TestMethod]
    public async Task APasswordOpensItAndIsNeverShown()
    {
        Directory.CreateDirectory(_library.FullPath("Secret"));
        File.Copy(TestSamples.ArchiveOf("tree-aes256.zip"), _library.FullPath("Secret/tree.zip"));

        var missing = await RunAsync(Extract("Secret/tree.zip", "Secret"));
        var wrong = await RunAsync(Extract("Secret/tree.zip", "Secret"), password: "wrong");
        var right = await RunAsync(Extract("Secret/tree.zip", "Secret"), password: "secret");

        Assert.AreEqual(FileOperationStatus.NeedsPassword, missing.Results.Single().Status);
        StringAssert.Contains(missing.Results.Single().Message, "protected by a password");
        Assert.AreEqual(FileOperationStatus.NeedsPassword, wrong.Results.Single().Status);
        StringAssert.Contains(wrong.Results.Single().Message, "doesn't open");
        Assert.AreEqual(FileOperationStatus.Done, right.Results.Single().Status, right.Results.Single().Message);
        Assert.IsTrue(File.Exists(_library.FullPath("Secret/tree/Deep/a/b/c/d/e/bottom.txt")));
        CollectionAssert.AreEquivalent(new[] { "tree", "tree.zip" }, Directory.EnumerateFileSystemEntries(_library.FullPath("Secret")).Select(Path.GetFileName).ToList(),
            "the failed ones left nothing");
        foreach (var view in new[] { missing, wrong, right })
            Assert.DoesNotContain("secret", JsonSerializer.Serialize(view).Replace("Secret/", ""));
        Assert.DoesNotContain("secret", _log.ToString().Replace("Secret", ""));
    }

    [TestMethod]
    public async Task ItNeedsTheRightsAtTheArchiveAndTheTarget()
    {
        Archive("Media/a.zip", ("x.txt", "x"));

        var noExtract = await RunAsync(Extract("Media/a.zip", "ChildDir"), new FakeAccess { Refuse = (path, right) => right == Rights.ExtractFiles && path == "ChildDir" });
        var noDownload = await RunAsync(Extract("Media/a.zip", "ChildDir"), new FakeAccess { Refuse = (path, right) => right == Rights.Download });
        var unseen = await RunAsync(Extract("Media/a.zip", "ChildDir"), new FakeAccess { Refuse = (path, right) => right == Rights.Browse && path == "Media/a.zip" });

        Assert.AreEqual(FileOperationStatus.Forbidden, noExtract.Results.Single().Status);
        Assert.AreEqual(FileOperationStatus.Forbidden, noDownload.Results.Single().Status);
        Assert.AreEqual(FileOperationStatus.NotFound, unseen.Results.Single().Status);
        Assert.IsFalse(Directory.Exists(_library.FullPath("ChildDir/a")));
    }

    [TestMethod]
    public async Task WhatIsNotAnArchiveSaysSo()
    {
        var job = await RunAsync(Extract("RFile1.txt", "ChildDir"));

        Assert.AreEqual(FileOperationStatus.Invalid, job.Results.Single().Status);
        StringAssert.Contains(job.Results.Single().Message, "not an archive");
    }

    [TestMethod]
    public async Task EntriesLeftOutAreListedInTheResult()
    {
        Archive("Media/slip.zip", ("../out.txt", "x"), ("CON.txt", "x"), ("ok.txt", "ok"));

        var job = await RunAsync(Extract("Media/slip.zip", "Media"));

        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        StringAssert.Contains(result.Message, "Left out 2 entries");
        StringAssert.Contains(result.Message, "“../out.txt”");
        Assert.IsFalse(File.Exists(_library.FullPath("out.txt")));
        Assert.IsTrue(File.Exists(_library.FullPath("Media/slip/ok.txt")));
    }

    [TestMethod]
    public async Task PartialFoldersLeftByAStopAreRemovedAtTheNextExtraction()
    {
        Archive("Media/a.zip", ("x.txt", "x"));
        _library.AddFile("Media/old" + FileJobs.PartialSuffix + "/half.bin", 10);

        await RunAsync(Extract("Media/a.zip", "Media"));

        Assert.IsFalse(Directory.Exists(_library.FullPath("Media/old" + FileJobs.PartialSuffix)));
        Assert.IsTrue(File.Exists(_library.FullPath("Media/a/x.txt")));
    }

    [TestMethod]
    public async Task ItKeepsTheFreeSpaceItIsToldTo()
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, s => s["Sfe:Files:MinimumFreeSpaceMegabytes"] = int.MaxValue.ToString(), _log.AddTo);
        Archive("Media/a.zip", ("x.txt", "x"));

        var job = await RunAsync(Extract("Media/a.zip", "Media"));

        Assert.AreEqual(FileOperationStatus.Failed, job.Results.Single().Status);
        StringAssert.Contains(job.Results.Single().Message, "MB free");
        Assert.IsFalse(Directory.Exists(_library.FullPath("Media/a")));
        Assert.IsEmpty(Directory.GetDirectories(_library.FullPath("Media"), "*" + FileJobs.PartialSuffix));
    }

    private static FileJobRequest Extract(string archive, string target) =>
        new(FileJobKind.Extract, [archive], target, NewFolder: true);

    private void Archive(string relative, params (string Name, string? Text)[] entries)
    {
        var full = _library.FullPath(relative);
        TestArchives.Zip(Path.GetDirectoryName(full)!, Path.GetFileName(full), entries);
    }

    private async Task<FileJobView> RunAsync(FileJobRequest request, IFileJobAccess? access = null, string? password = null)
    {
        var view = Jobs.Start(request, Me, access ?? new FakeAccess(), password);
        await Jobs.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        return Jobs.Find(view.Id)!;
    }

    private async Task<FileJobView> WaitForAsync(int id, Func<FileJobView, bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            if (Jobs.Find(id) is { } view && condition(view))
                return view;
            await Task.Delay(20, Token);
        }
        throw new TimeoutException(_log.ToString());
    }

    /// <summary>The scans the jobs started, finished.</summary>
    private async Task ScansAsync()
    {
        if (_services.Get<ScanCoordinator>().Completion is { } scan)
            await scan.WaitAsync(TimeSpan.FromSeconds(30), Token);
    }

    private Task<SfeDbContext> DbAsync() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);

    /// <summary>Everything allowed but what <see cref="Refuse"/> says.</summary>
    private sealed class FakeAccess : IFileJobAccess
    {
        public Func<string, string, bool> Refuse { get; init; } = (_, _) => false;

        public Task<bool> MayAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public Task<bool> MayThroughoutAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public bool IsProtected(string fullPath) => fullPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal);
    }
}
