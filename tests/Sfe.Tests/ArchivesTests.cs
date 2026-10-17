using System.IO.Compression;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Zips of several files or folders in Core (<see cref="Archives"/>).</summary>
[TestClass]
public sealed class ArchivesTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly List<string> _links = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;
    private Archives Archives => _services.Get<Archives>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        // Links first: deleting the library must not follow them.
        foreach (var link in _links)
            Directory.Delete(link);
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFolderIsZippedWithItsTreeItsEmptyFoldersAndItsContent()
    {
        Directory.CreateDirectory(_library.FullPath("RepeatedDir/Empty"));

        var (plan, zip) = await ZipAsync(new FakeAccess(), "RepeatedDir");

        Assert.AreEqual("RepeatedDir.zip", plan.FileName);
        CollectionAssert.IsSubsetOf(
            new[] { "RepeatedDir/", "RepeatedDir/RDFile1.txt", "RepeatedDir/ChildDir/", "RepeatedDir/ChildDir/AFile1.txt", "RepeatedDir/RepeatedDir/RDFile2.txt", "RepeatedDir/Empty/" },
            Names(zip));
        Assert.HasCount(10, Names(zip));
        using var reader = new StreamReader(zip.GetEntry("RepeatedDir/ChildDir/AFile1.txt")!.Open());
        Assert.AreEqual(await File.ReadAllTextAsync(_library.FullPath("RepeatedDir/ChildDir/AFile1.txt"), Token), await reader.ReadToEndAsync(Token));
        Assert.IsNull(zip.GetEntry(Archives.LeftOutEntry), "Nothing left out, no note");
    }

    [TestMethod]
    public async Task MediaIsStoredAndTextCompressed()
    {
        _library.AddFile("Media/notes.txt", 100_000);

        var (_, zip) = await ZipAsync(new FakeAccess(), "Media");

        var video = zip.GetEntry("Media/Holiday 2020.mp4")!;
        Assert.AreEqual(video.Length, video.CompressedLength, "Stored as it is");
        var text = zip.GetEntry("Media/notes.txt")!;
        Assert.IsLessThan(text.Length / 10, text.CompressedLength);
    }

    [TestMethod]
    public async Task ItemsOfTheSameNameAreKeptBothAndItemsInsideAChosenFolderOnce()
    {
        var (plan, zip) = await ZipAsync(new FakeAccess(), "ChildDir", "RepeatedDir/ChildDir", "ChildDir/AFile1.txt", "RFile1.txt", "RFile1.txt");

        Assert.AreEqual("Root.zip", plan.FileName, "Items of several folders: named after the library");
        Assert.HasCount(3, plan.Items);
        CollectionAssert.IsSubsetOf(new[] { "ChildDir/AFile1.txt", "ChildDir (2)/AFile1.txt", "RFile1.txt" }, Names(zip));
        Assert.AreEqual(1, Names(zip).Count(name => name == "ChildDir/AFile1.txt"));
    }

    [TestMethod]
    public void SeveralItemsOfOneFolderAreNamedAfterIt()
    {
        var plan = Plan(new FakeAccess(), "RepeatedDir/RDFile1.txt", "RepeatedDir/ChildDir");

        Assert.AreEqual("RepeatedDir.zip", plan.FileName);
        Assert.AreEqual("RDFile1.zip", Plan(new FakeAccess(), "RepeatedDir/RDFile1.txt").FileName);
    }

    [TestMethod]
    public async Task HiddenFoldersAreLeftOutSilentlyAndRefusedOnesListed()
    {
        var access = new FakeAccess
        {
            Verdict = path => path switch
            {
                "SpecialDir" => ArchiveVerdict.Hidden,
                "RepeatedDir/RepeatedDir" => ArchiveVerdict.Refused,
                _ => ArchiveVerdict.Allowed,
            },
        };

        var (plan, zip) = await ZipAsync(access, "");

        Assert.AreEqual("Root.zip", plan.FileName);
        Assert.IsFalse(Names(zip).Any(name => name.Contains("SpecialDir") || name.Contains("SDEspañol")));
        Assert.IsFalse(Names(zip).Any(name => name.StartsWith("Root/RepeatedDir/RepeatedDir/", StringComparison.Ordinal)));
        Assert.Contains("Root/RepeatedDir/RDFile1.txt", Names(zip));
        var note = await NoteAsync(zip);
        Assert.Contains("RepeatedDir/RepeatedDir/", note);
        Assert.DoesNotContain("SpecialDir", note, "A hidden folder is never named");
    }

    [TestMethod]
    public async Task TheWorkFolderAndTheServerFilesAreLeftOutAndLinksNotFollowed()
    {
        _library.AddFile(".sfe/trash/1/deleted.txt", 10);
        _library.AddFile("ChildDir/keys.xml", 10);
        var outside = Path.Combine(_library.BasePath, "Outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        if (!Link("ChildDir/Linked", outside))
            Assert.Inconclusive("Links can't be made here.");
        var access = new FakeAccess { Protected = real => real.EndsWith("keys.xml", StringComparison.Ordinal) };

        var (_, zip) = await ZipAsync(access, "");

        Assert.IsFalse(Names(zip).Any(name => name.Contains(".sfe") || name.Contains("deleted.txt")), "The work folder");
        Assert.IsFalse(Names(zip).Any(name => name.Contains("keys.xml")), "The server's files");
        Assert.IsFalse(Names(zip).Any(name => name.Contains("secret.txt")), "Links are not followed");
        var note = await NoteAsync(zip);
        Assert.Contains("ChildDir/Linked", note);
        Assert.DoesNotContain("keys.xml", note);
    }

    [TestMethod]
    public void ChosenItemsAreChecked()
    {
        var access = new FakeAccess
        {
            Verdict = path => path switch { "SpecialDir" => ArchiveVerdict.Hidden, "Media" => ArchiveVerdict.Refused, _ => ArchiveVerdict.Allowed },
        };
        _library.AddFile(".sfe/trash/x.txt", 10);

        Assert.AreEqual(FileOperationStatus.Invalid, Archives.Plan([], access).Status);
        Assert.AreEqual(FileOperationStatus.NotFound, Archives.Plan(["Nothing here"], access).Status);
        Assert.AreEqual(FileOperationStatus.NotFound, Archives.Plan(["../Root/RFile1.txt"], access).Status);
        Assert.AreEqual(FileOperationStatus.NotFound, Archives.Plan([".sfe/trash/x.txt"], access).Status);
        Assert.AreEqual(FileOperationStatus.NotFound, Archives.Plan(["RFile1.txt", "SpecialDir"], access).Status);
        Assert.AreEqual(FileOperationStatus.Forbidden, Archives.Plan(["Media"], access).Status);
        Assert.AreEqual(FileOperationStatus.Invalid, Archives.Plan([.. Enumerable.Repeat("RFile1.txt", Archives.MaxItems + 1)], access).Status);
    }

    [TestMethod]
    public async Task AZipStoppedByTheClientStopsReading()
    {
        var plan = Plan(new FakeAccess(), "");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var output = new OneWayStream(new MemoryStream()) { AfterWrite = written => { if (written > 1000) cancel.Cancel(); } };

        await Assert.ThrowsAsync<OperationCanceledException>(() => Archives.WriteAsync(plan, output, new FakeAccess(), cancel.Token));

        Assert.IsLessThan(2_000_000, output.Written, "Stopped long before the 5 MB of the library");
    }

    [TestMethod]
    public async Task TheLimitIsCheckedFromTheIndexAndWhileWriting()
    {
        await using var services = await CoreServices.CreateAsync(_library, s => s["Sfe:Files:MaxArchiveMegabytes"] = "1");
        var scans = services.Get<ScanCoordinator>();
        Assert.IsTrue(scans.TryStart(""));
        await scans.Completion!;
        var archives = services.Get<Archives>();
        var media = archives.Plan(["Media", "RFile2"], new FakeAccess()).Plan!;

        var size = await archives.IndexedSizeAsync(media, Token);

        Assert.AreEqual(5 * 1024 * 1024 + 300 * 1024 + 200 * 1024 + 3 * 1024 + 2 * 1024, size);
        Assert.AreEqual(1024 * 1024, archives.MaxBytes);
        await Assert.ThrowsAsync<ArchiveTooLargeException>(() => archives.WriteAsync(media, new OneWayStream(Stream.Null), new FakeAccess(), Token));
    }

    [TestMethod]
    public void BackslashesInNamesAreNotFolders()
    {
        Assert.AreEqual(".._.._evil.txt", Archives.EntryName(@"..\..\evil.txt"));
    }

    private ArchivePlan Plan(IArchiveAccess access, params string[] paths)
    {
        var result = Archives.Plan(paths, access);
        Assert.AreEqual(FileOperationStatus.Done, result.Status, result.Message);
        return result.Plan!;
    }

    /// <summary>Writes the zip to a stream that can't seek (as a response), then opens it.</summary>
    private async Task<(ArchivePlan Plan, ZipArchive Zip)> ZipAsync(IArchiveAccess access, params string[] paths)
    {
        var plan = Plan(access, paths);
        var buffer = new MemoryStream();
        await Archives.WriteAsync(plan, new OneWayStream(buffer), access, Token);
        buffer.Position = 0;
        return (plan, new ZipArchive(buffer, ZipArchiveMode.Read));
    }

    private static List<string> Names(ZipArchive zip) => [.. zip.Entries.Select(entry => entry.FullName)];

    private async Task<string> NoteAsync(ZipArchive zip)
    {
        var entry = zip.GetEntry(Archives.LeftOutEntry);
        Assert.IsNotNull(entry, "Something was left out: the zip says what");
        using var reader = new StreamReader(entry.Open());
        return await reader.ReadToEndAsync(Token);
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

    private sealed class FakeAccess : IArchiveAccess
    {
        public Func<string, ArchiveVerdict> Verdict { get; init; } = _ => ArchiveVerdict.Allowed;
        public Func<string, bool> Protected { get; init; } = _ => false;

        public ArchiveVerdict Check(string path) => Verdict(path);

        public bool IsProtected(string fullPath) => IsProtectedReal(fullPath);

        public bool IsProtectedReal(string realPath) =>
            realPath.Contains($"{Path.DirectorySeparatorChar}.sfe", StringComparison.Ordinal) || Protected(realPath);
    }

    /// <summary>A stream written in one direction only, as a response is.</summary>
    private sealed class OneWayStream(Stream inner) : Stream
    {
        public long Written { get; private set; }
        public Action<long>? AfterWrite { get; init; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Written += count;
            AfterWrite?.Invoke(Written);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            Written += buffer.Length;
            AfterWrite?.Invoke(Written);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
