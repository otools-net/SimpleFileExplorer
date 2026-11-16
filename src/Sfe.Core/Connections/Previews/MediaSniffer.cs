using System.Text;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// Tells a picture, video or audio file by its first bytes, never by its name: a file of a connection is anyone's
/// content, and a playlist, a concatenation script, a page or a drawing named <c>.mp4</c> must never reach ffmpeg
/// (agents/docs/development/security-audit-2026-11.md, A4). Only containers and formats of real media are known;
/// anything else (text above all) is not media.
/// </summary>
public static class MediaSniffer
{
    /// <summary>Bytes read at the start of a file: enough for every signature below.</summary>
    public const int HeadLength = 64;

    /// <summary>MPEG transport stream packets: a sync byte every 188 bytes.</summary>
    private const int TransportPacket = 188;

    /// <summary>
    /// The kind of media <paramref name="head"/> (the file's first bytes) starts; null when it is none.
    /// </summary>
    public static ItemKind? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length < 12)
            return null;
        return Picture(head) ?? Container(head) ?? Sound(head);
    }

    /// <summary>The kind of media the file at <paramref name="fullPath"/> starts with; null when none.</summary>
    public static ItemKind? DetectFile(string fullPath)
    {
        Span<byte> head = stackalloc byte[TransportPacket + 1];
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..read];
        if (read > TransportPacket && head[0] == 0x47 && head[TransportPacket] == 0x47)
            return ItemKind.Video;
        return Detect(head);
    }

    private static ItemKind? Picture(ReadOnlySpan<byte> head)
    {
        var known =
            Starts(head, [0xFF, 0xD8, 0xFF])
            || Starts(head, [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A])
            || Ascii(head, 0, "GIF87a")
            || Ascii(head, 0, "GIF89a")
            || (Ascii(head, 0, "RIFF") && Ascii(head, 8, "WEBP"))
            || Starts(head, [(byte)'I', (byte)'I', 0x2A, 0x00])
            || Starts(head, [(byte)'M', (byte)'M', 0x00, 0x2A])
            || (Ascii(head, 0, "BM") && head[6] == 0 && head[7] == 0 && head[8] == 0 && head[9] == 0)
            || (Ascii(head, 4, "ftyp") && IsPictureBrand(head));
        return known ? ItemKind.Image : null;
    }

    private static ItemKind? Container(ReadOnlySpan<byte> head)
    {
        var video =
            Ascii(head, 4, "ftyp")
            || Ascii(head, 4, "moov")
            || Ascii(head, 4, "mdat")
            || Ascii(head, 4, "wide")
            || Starts(head, [0x1A, 0x45, 0xDF, 0xA3])
            || (Ascii(head, 0, "RIFF") && Ascii(head, 8, "AVI "))
            || Starts(head, [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11])
            || Ascii(head, 0, "FLV\u0001")
            || Starts(head, [0x00, 0x00, 0x01, 0xBA])
            || Starts(head, [0x00, 0x00, 0x01, 0xB3])
            || Ascii(head, 0, ".RMF");
        return video ? ItemKind.Video : null;
    }

    private static ItemKind? Sound(ReadOnlySpan<byte> head)
    {
        var sound =
            Ascii(head, 0, "ID3")
            || (Ascii(head, 0, "RIFF") && Ascii(head, 8, "WAVE"))
            || Ascii(head, 0, "fLaC")
            || Ascii(head, 0, "OggS")
            || (Ascii(head, 0, "FORM") && (Ascii(head, 8, "AIFF") || Ascii(head, 8, "AIFC")))
            || Ascii(head, 0, "MAC ")
            || Ascii(head, 0, "wvpk")
            || Ascii(head, 0, "#!AMR")
            || IsMpegAudioFrame(head);
        return sound ? ItemKind.Audio : null;
    }

    /// <summary>
    /// An MPEG audio frame or an ADTS (AAC) header: 11 sync bits, then a version and layer that exist. Not the
    /// UTF-16 byte order mark <c>FF FE</c>, which is text.
    /// </summary>
    private static bool IsMpegAudioFrame(ReadOnlySpan<byte> head)
    {
        if (head[0] != 0xFF || (head[1] & 0xE0) != 0xE0 || head[1] == 0xFE || (head[2] & 0xF0) == 0xF0)
            return false;
        var adts = (head[1] & 0xF6) == 0xF0;
        var mpeg = (head[1] & 0x06) != 0 && ((head[1] >> 3) & 0x03) != 0x01;
        return adts || mpeg;
    }

    /// <summary>HEIC, HEIF and AVIF are ISO files whose brand names a picture.</summary>
    private static bool IsPictureBrand(ReadOnlySpan<byte> head) =>
        Ascii(head, 8, "heic")
        || Ascii(head, 8, "heix")
        || Ascii(head, 8, "mif1")
        || Ascii(head, 8, "msf1")
        || Ascii(head, 8, "avif")
        || Ascii(head, 8, "avis");

    private static bool Starts(ReadOnlySpan<byte> head, ReadOnlySpan<byte> signature) => head.StartsWith(signature);

    private static bool Ascii(ReadOnlySpan<byte> head, int offset, string text) =>
        head.Length >= offset + text.Length
        && head.Slice(offset, text.Length).SequenceEqual(Encoding.ASCII.GetBytes(text));
}
