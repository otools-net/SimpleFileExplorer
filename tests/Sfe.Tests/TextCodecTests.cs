using System.Text;
using Sfe.Core.Files;

namespace Sfe.Tests;

/// <summary>Bytes read and written as text the way an OS text editor does (<see cref="TextCodec"/>), and versions of content.</summary>
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
    [DataRow(TextCodec.Utf8)]
    [DataRow(TextCodec.Utf8Bom)]
    [DataRow(TextCodec.Utf16Le)]
    [DataRow(TextCodec.Utf16Be)]
    [DataRow(TextCodec.Windows1252)]
    public void EncodedTextReadsBackTheSame(string encoding)
    {
        const string text = "Café — “quoted” €5\nsecond line\n";

        var encoded = TextCodec.Encode(text, encoding, "crlf");
        var decoded = TextCodec.Decode(encoded.Bytes)!;

        Assert.AreEqual((text.Replace("\n", "\r\n"), encoding, "crlf"), (decoded.Text, decoded.Encoding, decoded.LineEnding));
    }

    [TestMethod]
    public void EveryLineEndsAsAsked()
    {
        Assert.AreEqual("a\nb\nc\n", Encoding.UTF8.GetString(TextCodec.Encode("a\r\nb\rc\n", TextCodec.Utf8, "lf").Bytes!));
        Assert.AreEqual("a\rb", Encoding.UTF8.GetString(TextCodec.Encode("a\nb", TextCodec.Utf8, "cr").Bytes!));
        Assert.AreEqual("crlf", TextCodec.DominantLineEnding("a\r\nb\r\nc\nd"));
        Assert.AreEqual("lf", TextCodec.DominantLineEnding("a\nb\nc\r\n"));
        Assert.AreEqual("lf", TextCodec.DominantLineEnding("no line"));
    }

    [TestMethod]
    public void Windows1252RefusesWhatItCantHoldAndSaysWhat()
    {
        var encoded = TextCodec.Encode("Tokyo 東京, Moscow Москва, 東京 again", TextCodec.Windows1252, "lf");

        Assert.IsNull(encoded.Bytes);
        CollectionAssert.AreEqual(new[] { "東", "京", "М", "о", "с", "к", "в", "а" }, encoded.Unrepresentable.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => TextCodec.Encode("x", "latin-9", "lf"));
        Assert.ThrowsExactly<ArgumentException>(() => TextCodec.Encode("x", TextCodec.Utf8, "mixed"));
    }

    [TestMethod]
    public void AFileCanBeReadInAnotherEncoding()
    {
        byte[] latin = [0x63, 0x61, 0x66, 0xC3, 0xA9];      // "café" in UTF-8, read as windows-1252: "cafÃ©"
        byte[] marked = [0xEF, 0xBB, 0xBF, 0x63, 0x61, 0x66, 0xC3, 0xA9];

        Assert.AreEqual(("cafÃ©", TextCodec.Windows1252), (TextCodec.Decode(latin, TextCodec.Windows1252)!.Text, TextCodec.Decode(latin, TextCodec.Windows1252)!.Encoding));
        Assert.AreEqual(("café", TextCodec.Utf8Bom), (TextCodec.Decode(marked, TextCodec.Utf8)!.Text, TextCodec.Decode(marked, TextCodec.Utf8)!.Encoding),
            "Read as UTF-8, its mark is kept for a save");
        Assert.AreEqual("\uFFFD", TextCodec.Decode([0xE9], TextCodec.Utf8)!.Text, "What isn't valid is replaced");
        Assert.AreEqual("ab", TextCodec.Decode([0x61, 0x00, 0x62, 0x00], TextCodec.Utf16Le)!.Text, "UTF-16 without a mark");
        Assert.IsNull(TextCodec.Decode([0x61, 0x00, 0x62, 0x00], TextCodec.Utf8), "binary for an 8-bit encoding");
        Assert.ThrowsExactly<ArgumentException>(() => TextCodec.Decode(latin, TextCodec.Utf8Bom));
    }

    [TestMethod]
    [DataRow("aaa\nbbb\nccc\nddd", false, false, "bbb\nccc\n")]
    [DataRow("aaa\nbbb\nccc\nddd", true, false, "aaa\nbbb\nccc\n")]
    [DataRow("aaa\nbbb\nccc\nddd", false, true, "bbb\nccc\nddd")]
    [DataRow("aaa\nbbb", true, true, "aaa\nbbb")]
    public void APartIsCutAtWholeLines(string text, bool atStart, bool atEnd, string kept)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var (from, to) = TextParts.Cut(bytes, atStart, atEnd, null);
        Assert.AreEqual(kept, Encoding.UTF8.GetString(bytes, from, to - from));
    }

    [TestMethod]
    public void APartWithoutLineBreaksIsCutAtWholeCharacters()
    {
        // "é€" cut inside both: the continuation byte at the start and the half "€" at the end go.
        byte[] bytes = [0xA9, 0x61, 0xC3, 0xA9, 0xE2, 0x82];
        var (from, to) = TextParts.Cut(bytes, atStart: false, atEnd: false, TextCodec.Utf8);
        Assert.AreEqual("aé", Encoding.UTF8.GetString(bytes, from, to - from));

        var wide = Encoding.Unicode.GetBytes("ab\ncd\nef");
        var (wideFrom, wideTo) = TextParts.Cut(wide, atStart: false, atEnd: false, TextCodec.Utf16Le);
        Assert.AreEqual("cd\n", Encoding.Unicode.GetString(wide, wideFrom, wideTo - wideFrom));
    }
}
