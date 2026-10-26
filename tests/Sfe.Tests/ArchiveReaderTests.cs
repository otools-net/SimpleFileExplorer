using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Sfe.Core.Files.Compression;
using Sfe.Core.Options;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Reading archives without unpacking them (<see cref="ArchiveReader"/>, <see cref="ArchiveFormats"/>,
/// <see cref="ArchiveEntryNames"/>): every format, and the archives a stranger makes to get out of their folder or to
/// fill the disk.
/// </summary>
[TestClass]
public sealed class ArchiveReaderTests
{
    private string _folder = "";

    [TestInitialize]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Delete() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void TheBytesDecideTheFormatAndTheNameTellsATarFromOneFile()
    {
        var zip = TestArchives.Zip(_folder, "a.zip", ("x.txt", "x"));
        Assert.AreEqual(ArchiveFormat.Zip, ArchiveFormats.Detect(zip));
        var renamed = Path.Combine(_folder, "really-a-7z.zip");
        File.Copy(TestSamples.ArchiveOf("tree-solid.7z"), renamed);
        Assert.AreEqual(ArchiveFormat.SevenZip, ArchiveFormats.Detect(renamed), "the bytes, not the name");

        var tgz = TestArchives.Tar(_folder, "b.tar.gz", gzip: true, ("x.txt", "x"));
        Assert.AreEqual(ArchiveFormat.TarGzip, ArchiveFormats.Detect(tgz));
        var gz = Path.Combine(_folder, "c.gz");
        File.Copy(tgz, gz);
        Assert.AreEqual(ArchiveFormat.Single, ArchiveFormats.Detect(gz), "a .gz is one file compressed alone");
        Assert.AreEqual(ArchiveFormat.Tar, ArchiveFormats.Detect(TestArchives.Tar(_folder, "d.tar", gzip: false, ("x.txt", "x"))));

        var text = Path.Combine(_folder, "not-a.zip");
        File.WriteAllText(text, "just text");
        Assert.IsNull(ArchiveFormats.Detect(text), "a name is not enough");
        Assert.IsNull(ArchiveFormats.OfName("zip"), "an extension alone is no name");
        Assert.AreEqual(ArchiveFormat.TarXz, ArchiveFormats.OfName("Backup.TAR.XZ"));
    }

    [TestMethod]
    public void ExploringIsOfferedOnlyWhereItWorks()
    {
        var options = new ArchiveOptions { MaxCompressedTarMegabytes = 10 };
        Assert.IsTrue(ArchiveFormats.IsExplorable(ArchiveFormat.Zip, long.MaxValue, options));
        Assert.IsTrue(ArchiveFormats.IsExplorable(ArchiveFormat.Rar, long.MaxValue, options));
        Assert.IsFalse(ArchiveFormats.IsExplorable(ArchiveFormat.Single, 1, options), "one file alone: extract only");
        Assert.IsTrue(ArchiveFormats.IsExplorable(ArchiveFormat.TarGzip, 10L * 1024 * 1024, options));
        Assert.IsFalse(ArchiveFormats.IsExplorable(ArchiveFormat.TarXz, 10L * 1024 * 1024 + 1, options), "listing it would decompress it all");
    }

    [TestMethod]
    [DataRow("../evil.txt", "goes up")]
    [DataRow("a/../../evil.txt", "goes up")]
    [DataRow("a\\..\\..\\evil.txt", "goes up")]
    [DataRow("/etc/passwd", "root")]
    [DataRow("\\\\server\\share\\x", "root")]
    [DataRow("C:\\Windows\\evil.dll", "drive")]
    [DataRow("C:evil.dll", "drive")]
    [DataRow("a\0b.txt", "control")]
    [DataRow("a/.../b.txt", "dots")]
    [DataRow("a/. ./b.txt", "dots")]
    [DataRow("", "empty")]
    [DataRow("./", "empty")]
    public void NamesThatLeaveTheirFolderAreRefused(string name, string why)
    {
        Assert.IsNull(ArchiveEntryNames.Split(name, 100, out var reason));
        Assert.Contains(why, reason!);
    }

    [TestMethod]
    public void NamesAreSplitIntoTheirFolders()
    {
        CollectionAssert.AreEqual(new[] { "a", "b", "c.txt" }, ArchiveEntryNames.Split("a/./b//c.txt", 100, out _));
        CollectionAssert.AreEqual(new[] { "a", "b.txt" }, ArchiveEntryNames.Split("a\\b.txt", 100, out _));
        CollectionAssert.AreEqual(new[] { "dossier", "año", "CON.txt" }, ArchiveEntryNames.Split("dossier/año/CON.txt", 100, out _),
            "names the system refuses are checked when extracting, not when listing");
        Assert.IsNotNull(ArchiveEntryNames.Split(string.Join('/', Enumerable.Repeat("d", 100)), 100, out _));
        Assert.IsNull(ArchiveEntryNames.Split(string.Join('/', Enumerable.Repeat("d", 101)), 100, out var reason));
        Assert.Contains("too deep", reason!);
        Assert.IsNull(ArchiveEntryNames.Split(new string('n', ArchiveEntryNames.MaxSegmentLength + 1), 100, out _));
    }

    [TestMethod]
    public void AZipIsListedFromItsDirectoryAndItsEntriesOpen()
    {
        var big = new string('z', 100_000);
        var zip = TestArchives.Zip(_folder, "a.zip", ("readme.txt", "hello"), ("Photos/", null), ("Photos/año/día.txt", "unicode"), ("Photos/big.txt", big));
        var entries = ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).ToList();

        CollectionAssert.AreEqual(new[] { "readme.txt", "Photos/", "Photos/año/día.txt", "Photos/big.txt" }, entries.Select(entry => entry.Name).ToList());
        Assert.AreEqual(ArchiveEntryKind.Folder, entries[1].Kind);
        Assert.AreEqual(100_000, entries[3].Size);
        Assert.IsLessThan(1000, entries[3].Packed, "deflated");
        Assert.IsNotNull(entries[0].Time);
        Assert.AreEqual("unicode", ReadText(zip, ArchiveFormat.Zip, entries[2]));
        Assert.AreEqual(big, ReadText(zip, ArchiveFormat.Zip, entries[3]));

        var stored = TestArchives.Zip(_folder, "stored.zip", CompressionLevel.NoCompression, ("s.txt", "stored as is"));
        var entry = ArchiveReader.ReadEntries(stored, ArchiveFormat.Zip).Single();
        Assert.AreEqual(0, entry.Method);
        Assert.AreEqual("stored as is", ReadText(stored, ArchiveFormat.Zip, entry));
    }

    [TestMethod]
    public void NamesWithoutTheUtf8FlagAreReadAsCodePage437()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(_folder, "dos.zip");
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, false, Encoding.GetEncoding(437)))
            zip.CreateEntry("über.txt");
        Assert.AreEqual("über.txt", ArchiveReader.ReadEntries(path, ArchiveFormat.Zip).Single().Name);
    }

    [TestMethod]
    public void AZipOfMoreThan65535EntriesIsReadThroughZip64()
    {
        var path = Path.Combine(_folder, "many.zip");
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            for (var i = 0; i < 70_000; i++)
                zip.CreateEntry($"f{i / 1000}/{i}.txt", CompressionLevel.NoCompression);

        var count = 0;
        foreach (var entry in ArchiveReader.ReadEntries(path, ArchiveFormat.Zip))
            Assert.AreEqual($"f{count / 1000}/{count++}.txt", entry.Name);
        Assert.AreEqual(70_000, count);
    }

    [TestMethod]
    public void AnEntryGivingMoreThanItDeclaresIsCutOffAsBroken()
    {
        // A zip bomb's trick: a directory claiming 1 KB for an entry that inflates to 1 MB.
        var zip = TestArchives.Zip(_folder, "bomb.zip", ("zeros.bin", new string('\0', 1024 * 1024)));
        TestArchives.PatchDirectory(zip, 24, BitConverter.GetBytes(1024u));
        var entry = ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).Single();
        Assert.AreEqual(1024, entry.Size);

        using var data = ArchiveReader.OpenEntry(zip, ArchiveFormat.Zip, entry);
        var error = Assert.Throws<ArchiveException>(() => data.CopyTo(Stream.Null));
        Assert.Contains("more data than", error.Message);
    }

    [TestMethod]
    public void AnEntryCutShortIsBroken()
    {
        var deflated = TestArchives.Zip(_folder, "short.zip", ("b.txt", new string('b', 5000)));
        TestArchives.PatchDirectory(deflated, 24, BitConverter.GetBytes(6000u));
        var entry = ArchiveReader.ReadEntries(deflated, ArchiveFormat.Zip).Single();

        using var data = ArchiveReader.OpenEntry(deflated, ArchiveFormat.Zip, entry);
        Assert.Contains("ends before", Assert.Throws<ArchiveException>(() => data.CopyTo(Stream.Null)).Message);
    }

    [TestMethod]
    public void ADirectoryThatLiesIsRefused()
    {
        var zip = TestArchives.Zip(_folder, "count.zip", ("a.txt", "a"));
        // The end record claims 60 000 entries in a directory of one.
        var bytes = File.ReadAllBytes(zip);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 10), 60_000);
        File.WriteAllBytes(zip, bytes);
        Assert.Contains("more entries than", Assert.Throws<ArchiveException>(() => ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).ToList()).Message);

        var outside = TestArchives.Zip(_folder, "offset.zip", ("a.txt", "a"));
        TestArchives.PatchDirectory(outside, 42, BitConverter.GetBytes(1_000_000u));
        var entry = ArchiveReader.ReadEntries(outside, ArchiveFormat.Zip).Single();
        Assert.Contains("outside", Assert.Throws<ArchiveException>(() => ArchiveReader.OpenEntry(outside, ArchiveFormat.Zip, entry)).Message);

        var cut = TestArchives.Zip(_folder, "cut.zip", ("a.txt", "a"));
        File.WriteAllBytes(cut, File.ReadAllBytes(cut)[..^10]);
        Assert.Throws<ArchiveException>(() => ArchiveReader.ReadEntries(cut, ArchiveFormat.Zip).ToList());
    }

    [TestMethod]
    public void EncryptedEntriesAreListedButNotOpened()
    {
        var zip = TestArchives.Zip(_folder, "secret.zip", ("a.txt", "a"));
        TestArchives.PatchDirectory(zip, 8, BitConverter.GetBytes((ushort)1)); // flags: encrypted
        var entry = ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).Single();
        Assert.IsTrue(entry.Encrypted);
        Assert.Contains("password", Assert.Throws<ArchiveException>(() => ArchiveReader.OpenEntry(zip, ArchiveFormat.Zip, entry)).Message);
    }

    [TestMethod]
    public void LinksMadeOnUnixAreListedAsLinks()
    {
        var zip = TestArchives.Zip(_folder, "link.zip", ("link", "/etc/passwd"));
        TestArchives.PatchDirectory(zip, 4, [0x14, 3]); // made by: Unix
        TestArchives.PatchDirectory(zip, 38, BitConverter.GetBytes(0xA1FFu << 16)); // mode: a symbolic link
        Assert.AreEqual(ArchiveEntryKind.Link, ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).Single().Kind);
    }

    [TestMethod]
    public void TarsAreReadForwardWithTheirLinks()
    {
        foreach (var gzip in new[] { false, true })
        {
            var path = Path.Combine(_folder, gzip ? "t.tar.gz" : "t.tar");
            using (var file = File.Create(path))
            using (Stream compressed = gzip ? new GZipStream(file, CompressionLevel.Fastest) : file)
            using (var tar = new TarWriter(compressed, TarEntryFormat.Pax))
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "docs/"));
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "docs/a.txt") { DataStream = new MemoryStream("first"u8.ToArray()) });
                tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "docs/passwd") { LinkName = "/etc/passwd" });
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "docs/b.txt") { DataStream = new MemoryStream("second"u8.ToArray()) });
            }
            var format = gzip ? ArchiveFormat.TarGzip : ArchiveFormat.Tar;
            var entries = ArchiveReader.ReadEntries(path, format).ToList();

            CollectionAssert.AreEqual(new[] { ArchiveEntryKind.Folder, ArchiveEntryKind.File, ArchiveEntryKind.Link, ArchiveEntryKind.File },
                entries.Select(entry => entry.Kind).ToList(), path);
            Assert.AreEqual("docs/b.txt", entries[3].Name.TrimStart('.', '/'));
            Assert.AreEqual("second", ReadText(path, format, entries[3]), "walked to, past the others");
            Assert.AreEqual("first", ReadText(path, format, entries[1]));
        }
    }

    [TestMethod]
    public void SevenZipsAreListedAndTheirFilesOpen()
    {
        var solid = TestSamples.ArchiveOf("tree-solid.7z");
        var entries = ArchiveReader.ReadEntries(solid, ArchiveFormat.SevenZip).ToList();
        var files = entries.Where(entry => entry.Kind == ArchiveEntryKind.File).ToDictionary(entry => entry.Name.Replace('\\', '/'));

        CollectionAssert.AreEquivalent(new[] { "readme.txt", "Photos/2024/día uno.txt", "Photos/2024/notes.txt", "Deep/a/b/c/d/e/bottom.txt" }, files.Keys.ToList());
        Assert.AreEqual(4097, files["Photos/2024/notes.txt"].Size);
        Assert.AreEqual(new string('x', 4096) + "\n", ReadText(solid, ArchiveFormat.SevenZip, files["Photos/2024/notes.txt"]));
        Assert.AreEqual("Five folders down.\n", ReadText(solid, ArchiveFormat.SevenZip, files["Deep/a/b/c/d/e/bottom.txt"]));
        Assert.IsTrue(entries.Any(entry => entry.Kind == ArchiveEntryKind.Folder && entry.Name.Replace('\\', '/') == "Deep/a/b"));
    }

    [TestMethod]
    [DataRow("Rar.rar")]
    [DataRow("Rar5.rar")]
    [DataRow("Rar5.solid.rar")]
    public void RarsAreReadForwardAndTheirFilesOpen(string name)
    {
        var rar = TestSamples.ThirdPartyOf("sharpcompress/" + name);
        Assert.AreEqual(ArchiveFormat.Rar, ArchiveFormats.Detect(rar));
        var files = ArchiveReader.ReadEntries(rar, ArchiveFormat.Rar).Where(entry => entry.Kind == ArchiveEntryKind.File)
            .ToDictionary(entry => entry.Name.Replace('\\', '/'));

        Assert.AreEqual(15_498, files["тест.txt"].Size);
        using var jpeg = ArchiveReader.OpenEntry(rar, ArchiveFormat.Rar, files["jpg/test.jpg"]);
        var start = new byte[2];
        jpeg.ReadExactly(start);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xD8 }, start, "a JPEG, walked to past the other entries");
        using var text = ArchiveReader.OpenEntry(rar, ArchiveFormat.Rar, files["тест.txt"]);
        Assert.AreEqual(15_498, new StreamReader(text).ReadToEnd().Length, "the whole entry, held to its size");
    }

    [TestMethod]
    public void EncryptedRarsAskForTheirPassword()
    {
        // SharpCompress reads even a RAR 5 whose names are in clear only with its password (0.50.7): listed, it says so.
        foreach (var name in new[] { "Rar5.encrypted_filesOnly.rar", "Rar5.encrypted_filesAndHeader.rar", "Rar.encrypted_filesAndHeader.rar" })
            Assert.Contains("password", Assert.Throws<ArchiveException>(
                () => ArchiveReader.ReadEntries(TestSamples.ThirdPartyOf("sharpcompress/" + name), ArchiveFormat.Rar).ToList()).Message, name);
    }

    [TestMethod]
    public void PasswordsAreSaidNotGuessed()
    {
        var filesEncrypted = TestSamples.ArchiveOf("tree-files-encrypted.7z");
        var entries = ArchiveReader.ReadEntries(filesEncrypted, ArchiveFormat.SevenZip).Where(entry => entry.Kind == ArchiveEntryKind.File).ToList();
        Assert.HasCount(4, entries, "the names are not encrypted: listed");
        Assert.IsTrue(entries.All(entry => entry.Encrypted));
        Assert.Contains("password", Assert.Throws<ArchiveException>(() => ArchiveReader.OpenEntry(filesEncrypted, ArchiveFormat.SevenZip, entries[0])).Message);

        var namesEncrypted = TestSamples.ArchiveOf("tree-names-encrypted.7z");
        Assert.Contains("password", Assert.Throws<ArchiveException>(() => ArchiveReader.ReadEntries(namesEncrypted, ArchiveFormat.SevenZip).ToList()).Message);
    }

    private static string ReadText(string path, ArchiveFormat format, ArchiveEntry entry)
    {
        using var data = ArchiveReader.OpenEntry(path, format, entry);
        using var reader = new StreamReader(data);
        return reader.ReadToEnd();
    }
}

/// <summary>Small archives written by the tests (zips and tars: .NET writes them), and the bytes of hostile ones.</summary>
internal static class TestArchives
{
    /// <summary>A zip of <paramref name="entries"/> (a null text = a folder entry).</summary>
    public static string Zip(string folder, string name, params (string Name, string? Text)[] entries) =>
        Zip(folder, name, CompressionLevel.Optimal, entries);

    public static string Zip(string folder, string name, CompressionLevel level, params (string Name, string? Text)[] entries)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (entryName, text) in entries)
        {
            var entry = zip.CreateEntry(entryName, level);
            if (text is null)
                continue;
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        return path;
    }

    /// <summary>A tar (or a .tar.gz) of files.</summary>
    public static string Tar(string folder, string name, bool gzip, params (string Name, string Text)[] files)
    {
        var path = Path.Combine(folder, name);
        using var file = File.Create(path);
        using Stream stream = gzip ? new GZipStream(file, CompressionLevel.Fastest) : file;
        using var tar = new TarWriter(stream, TarEntryFormat.Pax);
        foreach (var (entryName, text) in files)
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
        return path;
    }

    /// <summary>Writes <paramref name="bytes"/> at <paramref name="offset"/> of a central-directory record (the first by default).</summary>
    public static void PatchDirectory(string zip, int offset, byte[] bytes, int record = 0)
    {
        var content = File.ReadAllBytes(zip);
        var at = -1;
        for (var i = 0; i <= record; i++)
        {
            var next = content.AsSpan(at + 1).IndexOf("PK\x01\x02"u8);
            Assert.IsGreaterThanOrEqualTo(0, next, $"central-directory record {i}");
            at += next + 1;
        }
        bytes.CopyTo(content, at + offset);
        File.WriteAllBytes(zip, content);
    }
}
