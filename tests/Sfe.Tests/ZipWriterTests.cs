using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using SharpCompress.Readers;
using Sfe.Core.Files.Compression;

namespace Sfe.Tests;

/// <summary>
/// SFE's own zips (<see cref="ZipWriter"/>): read back by our reader, .NET's <c>ZipArchive</c>, SharpCompress and 7-Zip
/// (when installed: env <c>SFE_TEST_SEVENZIP</c>, else <c>C:/Program Files/7-Zip/7z.exe</c>), at every level, with and
/// without a password, past the limits of plain zips.
/// </summary>
[TestClass]
public sealed class ZipWriterTests
{
    private static readonly DateTime Time = new(2024, 5, 6, 7, 8, 10, DateTimeKind.Local);

    private static readonly (string Name, string Text)[] Tree =
    [
        ("readme.txt", "An archive made by SFE.\n"),
        ("Photos/2024/día uno.txt", "Unicode names survive.\n"),
        ("Photos/2024/notes.txt", string.Concat(Enumerable.Repeat("The same line, again and again.\n", 2000))),
        ("empty.txt", ""),
    ];

    private string _folder = "";

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Create()
    {
        _folder = Path.Combine(Path.GetTempPath(), "sfe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Delete() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(6)]
    [DataRow(9)]
    public async Task EveryLevelIsReadBackByEveryReader(int level)
    {
        var zip = await WriteAsync($"tree-{level}.zip", level);

        // .NET's own reader.
        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.AreEqual("Photos/", archive.GetEntry("Photos/")!.FullName);
            foreach (var (name, text) in Tree)
            {
                var entry = archive.GetEntry(name)!;
                using var reader = new StreamReader(entry.Open());
                Assert.AreEqual(text, reader.ReadToEnd(), name);
                Assert.AreEqual(Time, entry.LastWriteTime.LocalDateTime, name);
            }
        }
        // Ours: names, sizes, data, its checksum checked.
        var ours = ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).ToDictionary(e => e.Name);
        Assert.AreEqual(level == 0 ? 0 : 8, ours["Photos/2024/notes.txt"].Method);
        foreach (var (name, text) in Tree)
        {
            using var data = new StreamReader(ArchiveReader.OpenEntry(zip, ArchiveFormat.Zip, ours[name]));
            Assert.AreEqual(text, data.ReadToEnd(), name);
        }
        // SharpCompress.
        var seen = new Dictionary<string, string>();
        using (var file = File.OpenRead(zip))
        using (var reader = ReaderFactory.OpenReader(file))
            while (reader.MoveToNextEntry())
                if (!reader.Entry.IsDirectory)
                    seen[reader.Entry.Key!] = new StreamReader(reader.OpenEntryStream()).ReadToEnd();
        CollectionAssert.AreEquivalent(Tree.Select(f => f.Name).ToList(), seen.Keys.ToList());
        Assert.AreEqual(Tree[2].Text, seen[Tree[2].Name]);
        SevenZipTests(zip, null);
    }

    [TestMethod]
    public async Task HigherLevelsMakeSmallerZips()
    {
        var sizes = new List<long>();
        foreach (var level in new[] { 0, 1, 6, 9 })
            sizes.Add(new FileInfo(await WriteAsync($"tree-{level}.zip", level)).Length);

        Assert.IsGreaterThan(sizes[1], sizes[0], "stored is the largest");
        for (var i = 2; i < sizes.Count; i++)
            Assert.IsLessThanOrEqualTo(sizes[i - 1], sizes[i], string.Join(", ", sizes));
    }

    [TestMethod]
    [DataRow(0, "Secret-pass 1")]
    [DataRow(6, "Secret-pass 1")]
    [DataRow(6, "pässwörd")]
    public async Task APasswordEncryptsTheFilesWithAes256(int level, string password)
    {
        var zip = await WriteAsync($"secret-{level}.zip", level, password);
        var bytes = await File.ReadAllBytesAsync(zip, TestContext.CancellationToken);
        Assert.AreEqual(-1, bytes.AsSpan().IndexOf("Unicode names survive"u8), "the content is not in clear");
        Assert.AreNotEqual(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes("día uno.txt")), "the names are");

        var entries = ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).Where(e => e.Kind == ArchiveEntryKind.File).ToList();
        Assert.IsTrue(entries.All(e => e.Encrypted && e.Cipher == ZipCipher.Aes && e.CipherInfo == 0x23 && e.Crc == 0), "AE-2, 256 bits, no CRC");
        foreach (var (name, text) in Tree)
        {
            using var data = new StreamReader(ArchiveReader.OpenEntry(zip, ArchiveFormat.Zip, entries.Single(e => e.Name == name), password));
            Assert.AreEqual(text, data.ReadToEnd(), name);
        }
        Assert.IsTrue(Assert.Throws<ArchiveException>(() => ArchiveReader.OpenEntry(zip, ArchiveFormat.Zip, entries[0], "wrong")).NeedsPassword);

        using (var file = File.OpenRead(zip))
        using (var reader = ReaderFactory.OpenReader(file, new ReaderOptions { Password = password }))
        {
            var read = 0;
            while (reader.MoveToNextEntry())
                if (!reader.Entry.IsDirectory && reader.Entry.Key == Tree[1].Name)
                {
                    Assert.AreEqual(Tree[1].Text, new StreamReader(reader.OpenEntryStream()).ReadToEnd());
                    read++;
                }
            Assert.AreEqual(1, read, "SharpCompress opens it with the password");
        }
        // 7-Zip takes only ASCII passwords for zips ("The parameter is incorrect" otherwise; 26.01): SFE writes UTF-8,
        // as SharpCompress and our reader read them.
        if (password.All(char.IsAscii))
            SevenZipTests(zip, password);
    }

    [TestMethod]
    public async Task ManyEntriesUseZip64()
    {
        var zip = Path.Combine(_folder, "many.zip");
        await using (var output = File.Create(zip))
        await using (var writer = new ZipWriter(output))
        {
            for (var i = 0; i < 70_000; i++)
                await writer.AddFileAsync($"f/{i:00000}.txt", Time, new MemoryStream([(byte)'x']), 1, cancel: TestContext.CancellationToken);
            writer.Finish();
        }

        using (var archive = ZipFile.OpenRead(zip))
            Assert.HasCount(70_000, archive.Entries);
        Assert.AreEqual(70_000, ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).Count());
        SevenZipTests(zip, null);
    }

    [TestMethod]
    public async Task AStreamOfUnknownLengthGetsZip64Sizes()
    {
        var zip = Path.Combine(_folder, "unknown.zip");
        await using (var output = File.Create(zip))
        await using (var writer = new ZipWriter(output))
        {
            await writer.AddFileAsync("piped.txt", Time, new UnknownLength(Encoding.UTF8.GetBytes("from a pipe")), 6, cancel: TestContext.CancellationToken);
            writer.Finish();
        }

        using (var archive = ZipFile.OpenRead(zip))
            Assert.AreEqual("from a pipe", new StreamReader(archive.GetEntry("piped.txt")!.Open()).ReadToEnd());
        var entry = ArchiveReader.ReadEntries(zip, ArchiveFormat.Zip).Single();
        Assert.AreEqual("from a pipe", new StreamReader(ArchiveReader.OpenEntry(zip, ArchiveFormat.Zip, entry)).ReadToEnd());
        SevenZipTests(zip, null);
    }

    [TestMethod]
    public async Task TheBytesReadAreCounted()
    {
        long read = 0;
        await using var output = new MemoryStream();
        await using var writer = new ZipWriter(output);
        var packed = await writer.AddFileAsync("a.txt", Time, new MemoryStream(new byte[3 << 20]), 9, n => read += n, TestContext.CancellationToken);

        Assert.AreEqual(3 << 20, read);
        Assert.IsLessThan(64 * 1024, packed, "zeros deflate to almost nothing");
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddFileAsync("b.txt", Time, new MemoryStream(), 10).GetAwaiter().GetResult());
    }

    private async Task<string> WriteAsync(string name, int level, string? password = null)
    {
        var zip = Path.Combine(_folder, name);
        await using var output = File.Create(zip);
        await using var writer = new ZipWriter(output, password);
        writer.AddFolder("Photos", Time);
        writer.AddFolder("Photos/2024", Time);
        foreach (var (entry, text) in Tree)
            await writer.AddFileAsync(entry, Time, new MemoryStream(Encoding.UTF8.GetBytes(text)), level, cancel: TestContext.CancellationToken);
        writer.Finish();
        return zip;
    }

    /// <summary>7-Zip's test of the zip (<c>7z t</c>), when it is installed.</summary>
    private static void SevenZipTests(string zip, string? password)
    {
        var sevenZip = Environment.GetEnvironmentVariable("SFE_TEST_SEVENZIP") is { Length: > 0 } set ? set
            : File.Exists("C:/Program Files/7-Zip/7z.exe") ? "C:/Program Files/7-Zip/7z.exe" : null;
        if (sevenZip is null)
            return;
        var start = new ProcessStartInfo(sevenZip) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("t");
        start.ArgumentList.Add(password is null ? "-p-" : "-p" + password);
        start.ArgumentList.Add(zip);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, output);
        Assert.Contains("Everything is Ok", output);
    }

    /// <summary>A stream that can't tell its length (a pipe).</summary>
    private sealed class UnknownLength(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
