namespace Sfe.Core.Files;

/// <summary>
/// Where a part of a text file read from anywhere is cut, so that it holds whole lines and whole characters.
/// </summary>
public static class TextParts
{
    /// <summary>
    /// The bytes of <paramref name="bytes"/> to keep: from after the first line break (unless
    /// <paramref name="atStart"/>) to after the last one (unless <paramref name="atEnd"/>). Without a line break, at
    /// whole characters only (a UTF-8 sequence is never cut). <paramref name="encoding"/> null or 8-bit: one byte a
    /// character unit.
    /// </summary>
    public static (int From, int To) Cut(ReadOnlySpan<byte> bytes, bool atStart, bool atEnd, string? encoding)
    {
        ReadOnlySpan<byte> lineBreak = encoding switch
        {
            TextCodec.Utf16Le => [(byte)'\n', 0],
            TextCodec.Utf16Be => [0, (byte)'\n'],
            _ => [(byte)'\n'],
        };
        var unit = lineBreak.Length;
        var from = 0;
        var to = bytes.Length;
        if (!atStart && IndexOf(bytes, lineBreak, unit) is var first and >= 0 && first + unit < to)
        {
            from = first + unit;
        }
        else if (!atStart && unit == 1 && encoding != TextCodec.Windows1252)
        {
            while (from < to && from < 3 && (bytes[from] & 0xC0) == 0x80)
                from++;
        }

        if (!atEnd && LastIndexOf(bytes[from..], lineBreak, unit) is var last and >= 0)
            to = from + last + unit;
        else if (!atEnd && unit == 1 && encoding != TextCodec.Windows1252)
            to = from + WholeUtf8(bytes[from..to]);
        return (from, to);
    }

    /// <summary>The first line break at a character boundary (UTF-16: an even offset).</summary>
    private static int IndexOf(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> lineBreak, int unit)
    {
        for (var i = 0; i + unit <= bytes.Length; i += unit)
        {
            if (bytes.Slice(i, unit).SequenceEqual(lineBreak))
                return i;
        }

        return -1;
    }

    private static int LastIndexOf(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> lineBreak, int unit)
    {
        for (var i = (bytes.Length - unit) / unit * unit; i >= 0; i -= unit)
        {
            if (bytes.Slice(i, unit).SequenceEqual(lineBreak))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// How many bytes of <paramref name="bytes"/> end at a whole UTF-8 character (an 8-bit text: all of them).
    /// </summary>
    private static int WholeUtf8(ReadOnlySpan<byte> bytes)
    {
        // Back over at most three continuation bytes to the start of the last sequence; keep it only when complete.
        var i = bytes.Length - 1;
        var back = 0;
        while (i >= 0 && back < 3 && (bytes[i] & 0xC0) == 0x80)
        {
            i--;
            back++;
        }
        if (i < 0 || bytes[i] < 0xC0)
            return bytes.Length;
        var needed =
            bytes[i] >= 0xF0 ? 4
            : bytes[i] >= 0xE0 ? 3
            : 2;
        return back + 1 >= needed ? bytes.Length : i;
    }
}
