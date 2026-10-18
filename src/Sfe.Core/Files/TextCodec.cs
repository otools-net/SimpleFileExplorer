using System.Text;

namespace Sfe.Core.Files;

/// <param name="Text">The content, line endings as they are in the file.</param>
/// <param name="Encoding">How the bytes were read: <see cref="TextCodec.Utf8"/>, <see cref="TextCodec.Utf8Bom"/>,
/// <see cref="TextCodec.Utf16Le"/>, <see cref="TextCodec.Utf16Be"/> or <see cref="TextCodec.Windows1252"/>.</param>
/// <param name="LineEnding"><c>lf</c>, <c>crlf</c>, <c>cr</c>, or <c>mixed</c> when several are used (<c>lf</c> without any).</param>
public sealed record DecodedText(string Text, string Encoding, string LineEnding);

/// <param name="Bytes">The encoded text; null when some characters can't be written in the encoding.</param>
/// <param name="Unrepresentable">Those characters (at most ten), when <paramref name="Bytes"/> is null.</param>
public sealed record EncodedText(byte[]? Bytes, IReadOnlyList<string> Unrepresentable);

/// <summary>
/// Reads the bytes of a text file as the text editors of an OS do (agents/docs/features/text-viewer.md): a byte order
/// mark says the encoding; without one the bytes are UTF-8 when they are valid UTF-8, else windows-1252 (what Notepad
/// calls ANSI on a western system). Content with a NUL byte near the start and no mark is binary.
/// </summary>
public static class TextCodec
{
    public const string Utf8 = "utf-8";
    public const string Utf8Bom = "utf-8-bom";
    public const string Utf16Le = "utf-16le";
    public const string Utf16Be = "utf-16be";
    public const string Windows1252 = "windows-1252";

    /// <summary>How many bytes at the start are looked at for a NUL (as git and grep do).</summary>
    public const int BinaryProbe = 8 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Ansi;
    private static readonly Encoding StrictAnsi;

    static TextCodec()
    {
        // windows-1252 is not Latin-1: they differ at 0x80-0x9F (€, quotes, dashes), so it needs the code pages.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Ansi = Encoding.GetEncoding(1252);
        StrictAnsi = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
    }

    /// <summary>The text of <paramref name="bytes"/>, or null when they are not text.</summary>
    public static DecodedText? Decode(ReadOnlySpan<byte> bytes)
    {
        string text;
        string encoding;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            (text, encoding) = (Encoding.UTF8.GetString(bytes[3..]), Utf8Bom);
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            (text, encoding) = (Encoding.Unicode.GetString(bytes[2..]), Utf16Le);
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            (text, encoding) = (Encoding.BigEndianUnicode.GetString(bytes[2..]), Utf16Be);
        else if (bytes[..Math.Min(bytes.Length, BinaryProbe)].Contains((byte)0))
            return null;
        else
        {
            try
            {
                (text, encoding) = (StrictUtf8.GetString(bytes), Utf8);
            }
            catch (DecoderFallbackException)
            {
                (text, encoding) = (Ansi.GetString(bytes), Windows1252);
            }
        }
        return new DecodedText(text, encoding, LineEndingOf(text));
    }

    /// <summary>The encodings <see cref="Encode"/> writes.</summary>
    public static readonly IReadOnlySet<string> Encodings = new HashSet<string> { Utf8, Utf8Bom, Utf16Le, Utf16Be, Windows1252 };

    /// <summary>The line endings <see cref="Encode"/> writes.</summary>
    public static readonly IReadOnlySet<string> LineEndings = new HashSet<string> { "lf", "crlf", "cr" };

    /// <summary>
    /// The bytes of <paramref name="text"/> in <paramref name="encoding"/> (with its byte order mark), every line ending
    /// made <paramref name="lineEnding"/> (<c>lf</c>, <c>crlf</c> or <c>cr</c>). Characters windows-1252 has no byte for
    /// are listed instead (at most ten, each once): nothing is replaced by a "?" behind the person's back.
    /// </summary>
    public static EncodedText Encode(string text, string encoding, string lineEnding)
    {
        if (!Encodings.Contains(encoding))
            throw new ArgumentException($"Unknown encoding {encoding}.", nameof(encoding));
        if (!LineEndings.Contains(lineEnding))
            throw new ArgumentException($"Unknown line ending {lineEnding}.", nameof(lineEnding));
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (lineEnding != "lf")
            lines = lines.Replace("\n", lineEnding == "crlf" ? "\r\n" : "\r");
        switch (encoding)
        {
            case Utf8:
                return new EncodedText(new UTF8Encoding(false).GetBytes(lines), []);
            case Utf8Bom:
                return new EncodedText([0xEF, 0xBB, 0xBF, .. new UTF8Encoding(false).GetBytes(lines)], []);
            case Utf16Le:
                return new EncodedText([0xFF, 0xFE, .. Encoding.Unicode.GetBytes(lines)], []);
            case Utf16Be:
                return new EncodedText([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(lines)], []);
            default:
                var strict = StrictAnsi;
                try
                {
                    return new EncodedText(strict.GetBytes(lines), []);
                }
                catch (EncoderFallbackException)
                {
                    var missing = new List<string>();
                    foreach (var rune in lines.EnumerateRunes())
                    {
                        var character = rune.ToString();
                        if (missing.Contains(character))
                            continue;
                        try
                        {
                            strict.GetBytes(character);
                        }
                        catch (EncoderFallbackException)
                        {
                            missing.Add(character);
                            if (missing.Count == 10)
                                break;
                        }
                    }
                    return new EncodedText(null, missing);
                }
        }
    }

    /// <summary>The line ending most lines of <paramref name="text"/> end with (<c>crlf</c> on a tie); <c>lf</c> without any.</summary>
    public static string DominantLineEnding(string text)
    {
        int lf = 0, crlf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lf++;
            else if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                    cr++;
            }
        }
        if (lf + crlf + cr == 0)
            return "lf";
        return crlf >= lf && crlf >= cr ? "crlf" : lf >= cr ? "lf" : "cr";
    }

    /// <summary>The line ending used by <paramref name="text"/>: <c>lf</c>, <c>crlf</c>, <c>cr</c> or <c>mixed</c>.</summary>
    public static string LineEndingOf(string text)
    {
        int lf = 0, crlf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lf++;
            else if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                    cr++;
            }
        }
        return ((lf > 0 ? 1 : 0) + (crlf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0)) switch
        {
            > 1 => "mixed",
            _ when crlf > 0 => "crlf",
            _ when cr > 0 => "cr",
            _ => "lf",
        };
    }
}
