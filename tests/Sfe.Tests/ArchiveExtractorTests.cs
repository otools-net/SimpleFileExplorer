using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Sfe.Core.Files.Compression;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Extracting archives (<see cref="ArchiveExtractor"/>, <see cref="ArchiveReader.ReadThrough"/>): every format, the
/// entries chosen, and what a stranger's archive does to write outside its folder, to fill the disk or to leave a broken
/// file looking whole.
/// </summary>
[TestClass]
public sealed class ArchiveExtractorTests
{
    private static readonly ExtractLimits Limits = new(MaxDepth: 100, MaxEntries: 1000, MaxCompressionRatio: 10_000, PortableNames: true);

    /// <summary>The files of the samples' tree (agents/tools/samples/make-archives.mjs).</summary>
    private static readonly string[] Tree = ["readme.txt", "Photos/2024/día uno.txt", "Photos/2024/notes.txt", "Deep/a/b/c/d/e/bottom.txt"];

    private string _folder = "";
    private string _into = "";

    [TestInitialize]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"));
        _into = Path.Combine(_folder, "out");
        Directory.CreateDirectory(_into);
    }

    [TestCleanup]
    public void Delete() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void AZipIsWrittenWholeWithItsTimes()
    {
        var zip = TestArchives.Zip(_folder, "a.zip", ("readme.txt", "hello"), ("Empty/", null), ("Photos/año/día.txt", "unicode"));
        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits);

        Assert.AreEqual(2, result.Files);
        Assert.AreEqual("hello", File.ReadAllText(Path.Combine(_into, "readme.txt")));
        Assert.AreEqual("unicode", File.ReadAllText(Path.Combine(_into, "Photos", "año", "día.txt")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_into, "Empty")), "a folder entry, even empty");
        Assert.AreEqual(0, result.LeftOutCount);
        Assert.IsNull(result.Stopped);
        Assert.IsLessThan(TimeSpan.FromMinutes(5), (DateTime.Now - File.GetLastWriteTime(Path.Combine(_into, "readme.txt"))).Duration(),
            "the entry's time (the zip was written now)");
    }

    [TestMethod]
    [DataRow("tree-solid.7z", ArchiveFormat.SevenZip, null)]
    [DataRow("tree-files-encrypted.7z", ArchiveFormat.SevenZip, "secret")]
    [DataRow("tree-names-encrypted.7z", ArchiveFormat.SevenZip, "secret")]
    [DataRow("tree-aes256.zip", ArchiveFormat.Zip, "secret")]
    [DataRow("tree-aes128.zip", ArchiveFormat.Zip, "secret")]
    [DataRow("tree-zipcrypto.zip", ArchiveFormat.Zip, "secret")]
    public void TheSamplesComeOutWhole(string sample, ArchiveFormat format, string? password)
    {
        var result = ArchiveExtractor.Extract(TestSamples.ArchiveOf(sample), format, password, _into, null, Limits);

        Assert.AreEqual(Tree.Length, result.Files, sample);
        Assert.AreEqual("An archive made for the tests.\n", File.ReadAllText(Path.Combine(_into, "readme.txt")).Replace("\r\n", "\n"), sample);
        Assert.AreEqual(4097, new FileInfo(Path.Combine(_into, "Photos", "2024", "notes.txt")).Length, sample);
        Assert.IsTrue(File.Exists(Path.Combine(_into, "Deep", "a", "b", "c", "d", "e", "bottom.txt")), sample);
        // 2024-05-06T07:08:10Z, the samples' time.
        Assert.AreEqual(2024, File.GetLastWriteTimeUtc(Path.Combine(_into, "readme.txt")).Year, sample);
    }

    [TestMethod]
    [DataRow("Rar.rar")]
    [DataRow("Rar5.rar")]
    [DataRow("Rar5.solid.rar")]
    public void RarsComeOutWhole(string name)
    {
        var rar = TestSamples.ThirdPartyOf("sharpcompress/" + name);
        var listed = ArchiveReader.ReadEntries(rar, ArchiveFormat.Rar).Count(entry => entry.Kind == ArchiveEntryKind.File);

        var result = ArchiveExtractor.Extract(rar, ArchiveFormat.Rar, null, _into, null, Limits);

        Assert.AreEqual(listed, result.Files, name);
        Assert.AreEqual(15_498, new FileInfo(Path.Combine(_into, "тест.txt")).Length, name);
        Assert.AreEqual(0xFF, File.ReadAllBytes(Path.Combine(_into, "jpg", "test.jpg"))[0], name);
    }

    [TestMethod]
    [DataRow("Rar5.encrypted_filesOnly.rar")]
    [DataRow("Rar5.encrypted_filesAndHeader.rar")]
    [DataRow("Rar.encrypted_filesAndHeader.rar")]
    public void EncryptedRarsComeOutWithTheirPassword(string name)
    {
        var rar = TestSamples.ThirdPartyOf("sharpcompress/" + name);
        var result = ArchiveExtractor.Extract(rar, ArchiveFormat.Rar, "test", _into, null, Limits);

        Assert.IsGreaterThan(0, result.Files, name);
        Assert.AreEqual(15_498, new FileInfo(Path.Combine(_into, "тест.txt")).Length, name);
    }

    [TestMethod]
    [DataRow("tree-aes256.zip", ArchiveFormat.Zip)]
    [DataRow("tree-zipcrypto.zip", ArchiveFormat.Zip)]
    [DataRow("tree-files-encrypted.7z", ArchiveFormat.SevenZip)]
    [DataRow("tree-names-encrypted.7z", ArchiveFormat.SevenZip)]
    public void AWrongOrMissingPasswordStopsIt(string sample, ArchiveFormat format)
    {
        var archive = TestSamples.ArchiveOf(sample);
        foreach (var password in new[] { "wrong", null })
        {
            var error = Assert.Throws<ArchiveException>(() => ArchiveExtractor.Extract(archive, format, password, _into, null, Limits), $"{sample} {password}");
            Assert.IsTrue(error.NeedsPassword, $"{sample} {password}: {error.Message}");
            // Every file is protected: none is written (not one decrypted into garbage).
            Assert.IsEmpty(Directory.EnumerateFiles(_into, "*", SearchOption.AllDirectories).ToList(), $"{sample} {password}");
            Directory.Delete(_into, recursive: true);
            Directory.CreateDirectory(_into);
        }
    }

    [TestMethod]
    public void TarsAndCompressedTarsComeOut()
    {
        foreach (var gzip in new[] { false, true })
        {
            var name = gzip ? "t.tar.gz" : "t.tar";
            var tar = TestArchives.Tar(_folder, name, gzip, ("a/one.txt", "one"), ("two.txt", "two"));
            var into = Path.Combine(_folder, name + "-out");
            Directory.CreateDirectory(into);

            var result = ArchiveExtractor.Extract(tar, gzip ? ArchiveFormat.TarGzip : ArchiveFormat.Tar, null, into, null, Limits);

            Assert.AreEqual(2, result.Files, name);
            Assert.AreEqual("one", File.ReadAllText(Path.Combine(into, "a", "one.txt")), name);
            Assert.IsNull(result.Stopped, name);
        }
    }

    [TestMethod]
    public void ACutTarKeepsWhatCameBeforeTheBreak()
    {
        var tar = TestArchives.Tar(_folder, "cut.tar", gzip: false, ("first.txt", "first"), ("second.txt", new string('s', 4000)));
        var bytes = File.ReadAllBytes(tar);
        // Into the second file's data (each entry: its pax header, its header, its data).
        File.WriteAllBytes(tar, bytes[..4584]);

        var result = ArchiveExtractor.Extract(tar, ArchiveFormat.Tar, null, _into, null, Limits);

        Assert.AreEqual("first", File.ReadAllText(Path.Combine(_into, "first.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(_into, "second.txt")), "half a file is not written");
        Assert.IsNotNull(result.Stopped);
    }

    [TestMethod]
    public void AGzOfOneFileIsThatFile()
    {
        var gz = Path.Combine(_folder, "notes.txt.gz");
        using (var file = File.Create(gz))
        using (var stream = new GZipStream(file, CompressionLevel.Fastest))
            stream.Write("one file"u8);

        var result = ArchiveExtractor.Extract(gz, ArchiveFormat.Single, null, _into, null, Limits);

        Assert.AreEqual(1, result.Files);
        Assert.AreEqual("one file", File.ReadAllText(Path.Combine(_into, "notes.txt")));
    }

    [TestMethod]
    public void TheEntriesChosenComeOutFromTheirOwnNames()
    {
        var zip = TestArchives.Zip(_folder, "a.zip", ("top.txt", "top"), ("Photos/2024/a.jpg", "a"), ("Photos/2024/b.jpg", "b"), ("Photos/2024/Trip/c.jpg", "c"),
            ("Photos/2025/d.jpg", "d"), ("photos/2024/trip/e.jpg", "e"));

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, ["Photos/2024/Trip", "Photos/2024/a.jpg"], Limits);

        Assert.AreEqual(3, result.Files);
        Assert.AreEqual("a", File.ReadAllText(Path.Combine(_into, "a.jpg")), "a file chosen: at the top");
        Assert.AreEqual("c", File.ReadAllText(Path.Combine(_into, "Trip", "c.jpg")), "a folder chosen: from its name");
        Assert.AreEqual("e", File.ReadAllText(Path.Combine(_into, "trip", "e.jpg")), "names match in any case, as the listing shows them");
        Assert.IsFalse(File.Exists(Path.Combine(_into, "b.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(_into, "top.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_into, "Photos")));
    }

    [TestMethod]
    public void NamesLeadingOutOfTheFolderAreLeftOutAndListed()
    {
        var zip = TestArchives.Zip(_folder, "slip.zip", ("../escaped.txt", "x"), ("a/../../escaped2.txt", "x"), ("/rooted.txt", "x"), ("C:/drive.txt", "x"),
            ("ok.txt", "fine"), ("CON.txt", "device"), ("what?.txt", "x"), ("dots.../x.txt", "x"));

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits);

        Assert.AreEqual(1, result.Files);
        Assert.AreEqual("fine", File.ReadAllText(Path.Combine(_into, "ok.txt")));
        Assert.AreEqual(7, result.LeftOutCount);
        Assert.Contains(l => l.Name == "../escaped.txt" && l.Reason.Contains(".."), result.LeftOut);
        Assert.Contains(l => l.Name == "CON.txt" && l.Reason.Contains("device"), result.LeftOut);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(_folder, "*escaped*", SearchOption.AllDirectories).ToList());
        CollectionAssert.AreEquivalent(new[] { "out", "slip.zip" }, Directory.EnumerateFileSystemEntries(_folder).Select(Path.GetFileName).ToList(),
            "nothing written beside the folder");
    }

    [TestMethod]
    public void LinksAreNeverMade()
    {
        var tar = Path.Combine(_folder, "links.tar");
        using (var file = File.Create(tar))
        using (var writer = new TarWriter(file, TarEntryFormat.Pax))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "escape") { LinkName = "../../.." });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "hard") { LinkName = "/etc/passwd" });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "escape/through.txt") { DataStream = new MemoryStream("x"u8.ToArray()) });
        }

        var result = ArchiveExtractor.Extract(tar, ArchiveFormat.Tar, null, _into, null, Limits);

        Assert.IsNull(new DirectoryInfo(Path.Combine(_into, "escape")).LinkTarget, "never a link");
        Assert.IsFalse(File.Exists(Path.Combine(_into, "hard")));
        Assert.Contains(l => l.Name == "escape" && l.Reason.Contains("link"), result.LeftOut);
        Assert.IsTrue(File.Exists(Path.Combine(_into, "escape", "through.txt")), "a file below the link's name is a plain file in a plain folder");
    }

    [TestMethod]
    public void AFileLyingAboutItsSizeIsNotWritten()
    {
        // The directory says 1 KB; the data inflates to 1 MB: a zip bomb's lie. Its CRC is the whole data's.
        var zip = TestArchives.Zip(_folder, "bomb.zip", ("zeros.bin", new string('\0', 1024 * 1024)), ("after.txt", "after"));
        TestArchives.PatchDirectory(zip, 24, BitConverter.GetBytes(1024u));

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits);

        Assert.IsFalse(File.Exists(Path.Combine(_into, "zeros.bin")), "its first KB is not left looking like the file");
        Assert.Contains(l => l.Name == "zeros.bin" && l.Reason.Contains("more data"), result.LeftOut);
        Assert.AreEqual("after", File.ReadAllText(Path.Combine(_into, "after.txt")), "the entries after it still come out");
    }

    [TestMethod]
    public void ADamagedFileIsNotWritten()
    {
        // Stored, so its bytes are in the zip as they are: one changed, its CRC no longer matches.
        var zip = TestArchives.Zip(_folder, "damaged.zip", CompressionLevel.NoCompression, ("data.txt", "the original content"));
        var bytes = File.ReadAllBytes(zip);
        var at = bytes.AsSpan().IndexOf("original"u8);
        bytes[at] = (byte)'O';
        File.WriteAllBytes(zip, bytes);

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits);

        Assert.IsFalse(File.Exists(Path.Combine(_into, "data.txt")));
        Assert.Contains(l => l.Reason.Contains("checksum"), result.LeftOut);
    }

    [TestMethod]
    public void ARatioPastTheLimitIsLeftOut()
    {
        var zip = TestArchives.Zip(_folder, "ratio.zip", ("zeros.bin", new string('\0', 1024 * 1024)), ("text.txt", "text"));

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits with { MaxCompressionRatio = 100 });

        Assert.IsFalse(File.Exists(Path.Combine(_into, "zeros.bin")));
        Assert.Contains(l => l.Name == "zeros.bin" && l.Reason.Contains("100"), result.LeftOut);
        Assert.IsTrue(File.Exists(Path.Combine(_into, "text.txt")));
    }

    [TestMethod]
    public void TheSameNameTwiceKeepsTheFirst()
    {
        var zip = TestArchives.Zip(_folder, "twice.zip", ("same.txt", "first"), ("same.txt", "second"), ("x", "a file"), ("x/inside.txt", "below a file"));

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits);

        Assert.AreEqual("first", File.ReadAllText(Path.Combine(_into, "same.txt")));
        Assert.AreEqual("a file", File.ReadAllText(Path.Combine(_into, "x")));
        Assert.AreEqual(2, result.LeftOutCount);
    }

    [TestMethod]
    public void TooManyEntriesStopIt()
    {
        var zip = TestArchives.Zip(_folder, "many.zip", [.. Enumerable.Range(0, 20).Select(i => ($"f{i:00}.txt", (string?)"x"))]);

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits with { MaxEntries = 5 });

        Assert.AreEqual(5, result.Files);
        Assert.Contains("5", result.Stopped!);
    }

    [TestMethod]
    public void TheDiskRefusingStopsIt()
    {
        var zip = TestArchives.Zip(_folder, "a.zip", ("a.txt", "a"), ("b.txt", "b"));
        var calls = 0;

        Assert.Throws<IOException>(() => ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits,
            reserve: _ => { if (++calls == 2) throw new IOException("full"); }));
        Assert.IsFalse(File.Exists(Path.Combine(_into, "b.txt")));
    }

    [TestMethod]
    public void ItStopsWhenCancelled()
    {
        var zip = TestArchives.Zip(_folder, "a.zip", ("a.txt", "a"), ("b.txt", new string('b', 3 << 20)));
        using var cancel = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() => ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits,
            written: _ => cancel.Cancel(), cancel: cancel.Token));
        Assert.IsFalse(File.Exists(Path.Combine(_into, "b.txt")), "the file being written goes");
    }

    [TestMethod]
    public void PathsPastWindowsOldLimitAreWritten()
    {
        // 260 characters was Windows' limit without long paths enabled: .NET writes past it by itself (its `\\?\` prefix).
        var folders = string.Join('/', Enumerable.Range(0, 4).Select(i => new string((char)('a' + i), 90)));
        var zip = TestArchives.Zip(_folder, "deep-names.zip", ($"{folders}/far.txt", "far"));

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits);

        var written = Path.Combine(_into, folders.Replace('/', Path.DirectorySeparatorChar), "far.txt");
        Assert.IsGreaterThan(300, written.Length);
        Assert.AreEqual((1, 0), (result.Files, result.LeftOutCount));
        Assert.AreEqual("far", File.ReadAllText(written));
    }

    [TestMethod]
    public void AnEntryAsLongAsTheIndexKeepsIsLeftOut()
    {
        var zip = TestArchives.Zip(_folder, "long.zip", ("short.txt", "s"), (new string('n', 150) + ".txt", "long"));

        var result = ArchiveExtractor.Extract(zip, ArchiveFormat.Zip, null, _into, null, Limits with { MaxFullPath = _into.Length + 100 });

        Assert.AreEqual(1, result.Files);
        Assert.Contains(l => l.Reason.Contains("index"), result.LeftOut);
    }
}
