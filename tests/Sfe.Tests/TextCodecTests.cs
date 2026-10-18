using System.Text;
using Sfe.Core.Files;

namespace Sfe.Tests;

/// <summary>Bytes read as text the way an OS text editor does (<see cref="TextCodec"/>), and versions of content.</summary>
[TestClass]
public sealed class TextCodecTests
{
    [TestMethod]
    public void ByteOrderMarksGiveTheEncoding()
    {
        var utf8 = TextCodec.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("héllo")])!;
        var le = TextCodec.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("héllo\r\n")])!;
        var be = TextCodec.Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("héllo")])!;

        Assert.AreEqual(("héllo", TextCodec.Utf8Bom), (utf8.Text, utf8.Encoding));
        Assert.AreEqual(("héllo\r\n", TextCodec.Utf16Le, "crlf"), (le.Text, le.Encoding, le.LineEnding), "UTF-16 is full of NULs, yet text");
        Assert.AreEqual(("héllo", TextCodec.Utf16Be), (be.Text, be.Encoding));
    }

    [TestMethod]
    public void ValidUtf8IsUtf8AndAnythingElseWindows1252()
    {
        var utf8 = TextCodec.Decode(Encoding.UTF8.GetBytes("Ünïcödé — 写真"))!;
        // "€ “quoted” – café" in windows-1252: 0x80 and 0x93/0x94/0x96 are not Latin-1's control characters.
        var ansi = TextCodec.Decode([0x80, 0x20, 0x93, 0x71, 0x94, 0x20, 0x96, 0x20, 0x63, 0x61, 0x66, 0xE9])!;

        Assert.AreEqual(("Ünïcödé — 写真", TextCodec.Utf8), (utf8.Text, utf8.Encoding));
        Assert.AreEqual(("€ “q” – café", TextCodec.Windows1252), (ansi.Text, ansi.Encoding));
        Assert.AreEqual(TextCodec.Utf8, TextCodec.Decode([])!.Encoding, "An empty file is text");
    }

    [TestMethod]
    public void ANulNearTheStartIsBinary()
    {
        Assert.IsNull(TextCodec.Decode([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]), "a PNG");
        var late = new byte[TextCodec.BinaryProbe + 10];
        late.AsSpan().Fill((byte)'a');
        late[^1] = 0;
        Assert.IsNotNull(TextCodec.Decode(late), "only the start is looked at");
    }

    [TestMethod]
    [DataRow("a\nb\n", "lf")]
    [DataRow("a\r\nb\r\n", "crlf")]
    [DataRow("a\rb\r", "cr")]
    [DataRow("a\r\nb\nc", "mixed")]
    [DataRow("one line", "lf")]
    [DataRow("", "lf")]
    public void TellsTheLineEnding(string text, string expected) => Assert.AreEqual(expected, TextCodec.LineEndingOf(text));

    [TestMethod]
    public void AVersionChangesWithTheBytesEvenAtTheSameTimeAndSize()
    {
        var time = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var first = FileVersion.Of("abc"u8, time);
        var same = FileVersion.Of("abc"u8, time);
        var edited = FileVersion.Of("abd"u8, time);

        Assert.AreEqual(first, same);
        Assert.AreNotEqual(first.Token, edited.Token);
        Assert.AreNotEqual(first.Token, FileVersion.Of("abc"u8, time.AddSeconds(2)).Token);
    }

    [TestMethod]
    public async Task ReadsUpToTheLimit()
    {
        var file = Path.Combine(Path.GetTempPath(), $"sfe-text-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "0123456789", TestContext.CancellationToken);
        try
        {
            var content = new FileContent();
            var whole = (await content.ReadAsync(file, 10, TestContext.CancellationToken))!;
            var tooLarge = (await content.ReadAsync(file, 9, TestContext.CancellationToken))!;

            Assert.AreEqual("0123456789", Encoding.ASCII.GetString(whole.Bytes!));
            Assert.AreEqual(10, whole.Version!.Size);
            Assert.IsTrue(tooLarge.TooLarge);
            Assert.AreEqual(10, tooLarge.Size);
            Assert.IsNull(await content.ReadAsync(file + ".missing", 10, TestContext.CancellationToken));
        }
        finally
        {
            File.Delete(file);
        }
    }

    public TestContext TestContext { get; set; } = null!;
}
