using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Files.Compression;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Zips made in the library as background jobs (<see cref="FileJobs"/>, <see cref="FileJobKind.Compress"/>). The walk is
/// the downloads' (links, rules, the note of what was left out: <c>ArchivesTests</c>); here, what the job adds.
/// </summary>
[TestClass]
public sealed class CompressJobTests
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
    public async Task AFolderGoesIntoAZipBesideItWhichIsIndexed()
    {
        var job = await RunAsync(Compress(["RepeatedDir"], ""));

        var result = job.Results.Single();
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        Assert.AreEqual("RepeatedDir.zip", result.NewPath);
        StringAssert.Contains(result.Message, "→");
        StringAssert.Contains(result.Message, "%");
        Assert.AreEqual(1, job.Total);
        var originals = Directory.GetFiles(_library.FullPath("RepeatedDir"), "*", SearchOption.AllDirectories)
            .Select(f => "RepeatedDir/" + Path.GetRelativePath(_library.FullPath("RepeatedDir"), f).Replace('\\', '/')).ToList();
        using (var zip = ZipFile.OpenRead(_library.FullPath("RepeatedDir.zip")))
            CollectionAssert.AreEquivalent(originals, zip.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName).ToList());
        Assert.AreEqual(originals.Count, ArchiveReader.ReadEntries(_library.FullPath("RepeatedDir.zip"), ArchiveFormat.Zip).Count(e => e.Kind == ArchiveEntryKind.File));
        Assert.AreEqual(job.BytesTotal, job.BytesDone, "every byte read");
        Assert.IsEmpty(Directory.GetFiles(_library.Root, "*" + FileJobs.PartialSuffix));
        await using var db = await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(Token);
        Assert.IsTrue(await db.Files.AnyAsync(f => f.FullName == _library.FullPath("RepeatedDir.zip"), Token), "the zip is indexed at once");
    }

    [TestMethod]
    public async Task SeveralItemsTakeTheNameAskedAndAZipInsideAChosenFolderIsNotPutIntoItself()
    {
        var job = await RunAsync(Compress(["RFile1.txt", "ChildDir"], "ChildDir") with { Name = "my stuff" });

        Assert.AreEqual("ChildDir/my stuff.zip", job.Results.Single().NewPath, job.Results.Single().Message);
        using var zip = ZipFile.OpenRead(_library.FullPath("ChildDir/my stuff.zip"));
        Assert.IsNotNull(zip.GetEntry("RFile1.txt"));
        Assert.IsFalse(zip.Entries.Any(e => e.Name.Contains("my stuff") || e.Name.Contains(FileJobs.PartialSuffix)), "not in itself");
    }

    [TestMethod]
    public async Task TheLevelAndTheFilesAlreadyCompressedAreAsAsked()
    {
        File.WriteAllText(_library.FullPath("Media/notes.txt"), string.Concat(Enumerable.Repeat("compress me ", 5000)));

        await RunAsync(Compress(["Media/notes.txt", "Media/photo.jpg"], "", "stored") with { Level = 0 });
        await RunAsync(Compress(["Media/notes.txt", "Media/photo.jpg"], "", "skipping"));
        await RunAsync(Compress(["Media/notes.txt", "Media/photo.jpg"], "", "everything") with { SkipCompressed = false });

        Dictionary<string, int> Methods(string zip) =>
            ArchiveReader.ReadEntries(_library.FullPath(zip), ArchiveFormat.Zip).ToDictionary(e => e.Name, e => e.Method);
        CollectionAssert.AreEquivalent(new Dictionary<string, int> { ["notes.txt"] = 0, ["photo.jpg"] = 0 }, Methods("stored.zip"));
        CollectionAssert.AreEquivalent(new Dictionary<string, int> { ["notes.txt"] = 8, ["photo.jpg"] = 0 }, Methods("skipping.zip"));
        CollectionAssert.AreEquivalent(new Dictionary<string, int> { ["notes.txt"] = 8, ["photo.jpg"] = 8 }, Methods("everything.zip"));
    }

    [TestMethod]
    public async Task APasswordEncryptsItAndIsNeverShown()
    {
        var job = await RunAsync(Compress(["RFile1.txt"], "ChildDir"), password: "zip-secret");

        Assert.AreEqual(FileOperationStatus.Done, job.Results.Single().Status, job.Results.Single().Message);
        StringAssert.Contains(job.Results.Single().Message, "encrypted");
        var zip = _library.FullPath("ChildDir/RFile1.zip");
        var entry = ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).Single();
        Assert.IsTrue(entry.Encrypted);
        using (var data = ArchiveReader.OpenEntry(zip, ArchiveFormat.Zip, entry, "zip-secret"))
            Assert.AreEqual(new FileInfo(_library.FullPath("RFile1.txt")).Length, data.CopyToCount());
        Assert.DoesNotContain("zip-secret", JsonSerializer.Serialize(Jobs.List(null)));
        Assert.DoesNotContain("zip-secret", _log.ToString());
    }

    [TestMethod]
    public async Task ANameTakenIsAskedAndKeepBothGivesANumber()
    {
        _library.AddFile("RFile1.zip", 5);

        var view = Jobs.Start(Compress(["RFile1.txt"], ""), Me, new FakeAccess());
        var waiting = await WaitForAsync(view.Id, j => j.State == FileJobState.Waiting);
        Assert.AreEqual("RFile1.zip", waiting.Conflict!.Existing.Name);
        Assert.IsTrue(Jobs.Answer(view.Id, ConflictChoice.KeepBoth, forAll: false));
        await Jobs.Completion(view.Id)!;

        Assert.AreEqual("RFile1 (2).zip", Jobs.Find(view.Id)!.Results.Single().NewPath);
        Assert.AreEqual(5, new FileInfo(_library.FullPath("RFile1.zip")).Length, "the one there is kept");
        Assert.IsEmpty(Directory.GetFiles(_library.Root, "*" + FileJobs.PartialSuffix));
    }

    [TestMethod]
    public async Task WhatThePersonMayNotSeeOrDownloadIsLeftOut()
    {
        var access = new FakeAccess
        {
            Walk = path => path == "RepeatedDir/ChildDir" ? ArchiveVerdict.Hidden : path == "RepeatedDir/RepeatedDir" ? ArchiveVerdict.Refused : ArchiveVerdict.Allowed,
        };

        var job = await RunAsync(Compress(["RepeatedDir"], ""), access);

        StringAssert.Contains(job.Results.Single().Message, "Left out 1 item");
        using var zip = ZipFile.OpenRead(_library.FullPath("RepeatedDir.zip"));
        Assert.IsFalse(zip.Entries.Any(e => e.FullName.StartsWith("RepeatedDir/ChildDir/") || e.FullName.StartsWith("RepeatedDir/RepeatedDir/")));
        Assert.IsNotNull(zip.GetEntry(Archives.LeftOutEntry));
    }

    [TestMethod]
    public async Task ItNeedsTheRightToCompressThereAndToDownloadTheItems()
    {
        var noCompress = await RunAsync(Compress(["RFile1.txt"], "ChildDir"), new FakeAccess { Refuse = (path, right) => right == Rights.CompressFiles });
        var noDownload = await RunAsync(Compress(["RFile1.txt"], "ChildDir"), new FakeAccess { Walk = _ => ArchiveVerdict.Refused });
        var gone = await RunAsync(Compress(["RFile1.txt"], "ChildDir"), new FakeAccess { Gone = true });

        Assert.AreEqual(FileOperationStatus.Forbidden, noCompress.Results.Single().Status);
        Assert.AreEqual(FileOperationStatus.Forbidden, noDownload.Results.Single().Status);
        Assert.AreEqual(FileOperationStatus.Forbidden, gone.Results.Single().Status);
        Assert.IsEmpty(Directory.GetFiles(_library.FullPath("ChildDir"), "*.zip"));
    }

    [TestMethod]
    public async Task ANameRefusedSaysSo()
    {
        var job = await RunAsync(Compress(["RFile1.txt"], "", "a:b"));

        Assert.AreEqual(FileOperationStatus.Invalid, job.Results.Single().Status);
    }

    [TestMethod]
    public async Task ItKeepsTheFreeSpaceItIsToldTo()
    {
        await _services.DisposeAsync();
        _services = await CoreServices.CreateAsync(_library, s => s["Sfe:Files:MinimumFreeSpaceMegabytes"] = int.MaxValue.ToString(), _log.AddTo);

        var job = await RunAsync(Compress(["RFile1.txt"], ""));

        Assert.AreEqual(FileOperationStatus.Failed, job.Results.Single().Status);
        StringAssert.Contains(job.Results.Single().Message, "MB free");
        Assert.IsFalse(File.Exists(_library.FullPath("RFile1.zip")));
    }

    [TestMethod]
    public async Task CancellingRemovesThePartialZip()
    {
        var big = _library.FullPath("Big/random.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(big)!);
        var bytes = new byte[96 << 20];
        new Random(5).NextBytes(bytes);
        await File.WriteAllBytesAsync(big, bytes, Token);

        var view = Jobs.Start(Compress(["Big"], "") with { Level = 9, SkipCompressed = false }, Me, new FakeAccess());
        await WaitForAsync(view.Id, j => j.BytesDone > 0);
        Assert.IsTrue(Jobs.Cancel(view.Id));
        await Jobs.Completion(view.Id)!;

        Assert.AreEqual(FileJobState.Cancelled, Jobs.Find(view.Id)!.State);
        Assert.IsFalse(File.Exists(_library.FullPath("Big.zip")));
        Assert.IsEmpty(Directory.GetFiles(_library.Root, "*" + FileJobs.PartialSuffix));
    }

    [TestMethod]
    public async Task OneCompressionRunsAtATime()
    {
        var gate = _services.Get<ArchiveBuildGate>().Compressions(1);
        await gate.WaitAsync(Token);
        FileJobView view;
        try
        {
            view = Jobs.Start(Compress(["RFile1.txt"], ""), Me, new FakeAccess());
            await Task.Delay(300, Token);
            Assert.IsFalse(File.Exists(_library.FullPath("RFile1.zip")), "waiting for its turn");
        }
        finally
        {
            gate.Release();
        }
        await Jobs.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.IsTrue(File.Exists(_library.FullPath("RFile1.zip")));
    }

    private static FileJobRequest Compress(string[] paths, string target, string? name = null) =>
        new(FileJobKind.Compress, paths, target, Name: name);

    private async Task<FileJobView> RunAsync(FileJobRequest request, IFileJobAccess? access = null, string? password = null)
    {
        var view = Jobs.Start(request, Me, access ?? new FakeAccess(), password);
        await Jobs.Completion(view.Id)!.WaitAsync(TimeSpan.FromSeconds(60), Token);
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

    /// <summary>Everything allowed but what <see cref="Refuse"/> and <see cref="Walk"/> say; <see cref="Gone"/>: the account is.</summary>
    private sealed class FakeAccess : IFileJobAccess, IArchiveAccess
    {
        public Func<string, string, bool> Refuse { get; init; } = (_, _) => false;
        public Func<string, ArchiveVerdict> Walk { get; init; } = _ => ArchiveVerdict.Allowed;
        public bool Gone { get; init; }

        public Task<bool> MayAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public Task<bool> MayThroughoutAsync(string path, string right) => Task.FromResult(!Refuse(path, right));

        public bool IsProtected(string fullPath) => fullPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal);

        public Task<IArchiveAccess?> ArchiveAccessAsync() => Task.FromResult<IArchiveAccess?>(Gone ? null : this);

        public ArchiveVerdict Check(string path) => Walk(path);

        public bool IsProtectedReal(string realPath) => IsProtected(realPath);
    }
}

internal static class StreamCounting
{
    /// <summary>Reads the stream to its end; the bytes it gave.</summary>
    public static long CopyToCount(this Stream stream)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer)) > 0)
            total += read;
        return total;
    }
}
