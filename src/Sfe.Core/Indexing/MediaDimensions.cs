using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sfe.Core.Library;
using Sfe.Core.Thumbnails;

namespace Sfe.Core.Indexing;

/// <summary>Width and height in pixels, as the picture is shown (rotated by its orientation metadata).</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    /// <summary>Stored for a file whose size could not be read, so that it is not read again until it changes.</summary>
    public static readonly PixelSize Unknown = new(0, 0);

    public bool IsKnown => Width > 0 && Height > 0;
}

/// <summary>What a scan reads of an image, video or audio file.</summary>
/// <param name="Size">The picture of an image or video; null for audio, which has none (a cover is not its picture).</param>
/// <param name="Duration">Seconds of a video or audio file; 0 when ffmpeg does not tell (a broken file); null for images.</param>
public readonly record struct MediaProperties(PixelSize? Size, double? Duration = null);

/// <summary>
/// Reads the resolution of images and videos, and the duration of videos and audio files, for the index. Common image
/// formats (JPEG, PNG, GIF, BMP, WebP) are read from their first bytes, without decoding; videos, audio and other
/// images go through ffmpeg (<c>ffmpeg -i</c> describes the streams), when it is available.
/// </summary>
public sealed partial class MediaDimensions(Ffmpeg ffmpeg, ILogger<MediaDimensions> logger)
{
    /// <summary>Describing a file takes ffmpeg a fraction of a second; more means a stuck drive.</summary>
    private static readonly TimeSpan FfmpegTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Whether <see cref="ReadAsync"/> can tell anything about files of this kind right now.</summary>
    public bool CanRead(ItemKind kind) => kind == ItemKind.Image || kind is ItemKind.Video or ItemKind.Audio && ffmpeg.IsAvailable;

    /// <returns>
    /// The size (<see cref="PixelSize.Unknown"/> when the file is not a picture it understands; none for audio) and, for
    /// a video or audio file, its duration; null when it cannot tell now (ffmpeg missing or timed out, file locked), so
    /// that the next scan tries again.
    /// </returns>
    public async Task<MediaProperties?> ReadAsync(string fullPath, ItemKind kind, CancellationToken cancellationToken = default)
    {
        try
        {
            if (kind == ItemKind.Image)
            {
                PixelSize? header;
                await using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: false))
                    header = ReadImageHeader(stream);
                if (header is { } size)
                    return new MediaProperties(size);
            }
            else if (kind is not (ItemKind.Video or ItemKind.Audio))
            {
                return null;
            }

            if (!ffmpeg.IsAvailable)
                return null;
            var output = await ProbeAsync(fullPath, cancellationToken);
            return kind switch
            {
                ItemKind.Image => new MediaProperties(ParseFfmpegOutput(output)),
                ItemKind.Video => new MediaProperties(ParseFfmpegOutput(output), ParseDuration(output)),
                _ => new MediaProperties(null, ParseDuration(output)),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            logger.LogWarning(ex, "Cannot read the resolution or duration of {File}", fullPath);
            return null;
        }
    }

    private async Task<string> ProbeAsync(string fullPath, CancellationToken cancellationToken)
    {
        // No output file: ffmpeg describes the input, then fails with "At least one output file must be specified".
        var (_, errors) = await ffmpeg.RunAsync(["-hide_banner", "-nostdin", "-i", fullPath], FfmpegTimeout, cancellationToken: cancellationToken);
        return errors;
    }

    /// <summary>
    /// Seconds of the input's "Duration: 01:23:45.67" line (the container's length); 0 when missing or "N/A" (a
    /// stream without length, a file ffmpeg does not understand).
    /// </summary>
    internal static double ParseDuration(string output)
    {
        var match = DurationLine().Match(output);
        if (!match.Success)
            return 0;
        var seconds = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
            + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
            + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return Math.Round(seconds, 2);
    }

    [GeneratedRegex(@"^\s*Duration: (\d+):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.Multiline)]
    private static partial Regex DurationLine();

    /// <summary>The first video stream that is not a cover picture, turned by its rotation metadata.</summary>
    internal static PixelSize ParseFfmpegOutput(string output)
    {
        var streams = StreamStart().Split(output);
        foreach (var stream in streams.Skip(1))
        {
            var description = stream.Split('\n', 2)[0];
            var video = VideoStream().Match(description);
            if (!video.Success || description.Contains("(attached pic)", StringComparison.Ordinal))
                continue;
            var dimensions = Dimensions().Match(video.Groups[1].Value);
            if (!dimensions.Success)
                continue;

            var size = new PixelSize(
                int.Parse(dimensions.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(dimensions.Groups[2].Value, CultureInfo.InvariantCulture));
            var rotation = Rotation().Match(stream);
            return rotation.Success && IsQuarterTurn(double.Parse(rotation.Groups[1].Value, CultureInfo.InvariantCulture))
                ? new PixelSize(size.Height, size.Width)
                : size;
        }

        return PixelSize.Unknown;
    }

    private static bool IsQuarterTurn(double degrees) => Math.Abs(Math.Round(degrees) % 180) == 90;

    [GeneratedRegex(@"^\s*Stream #", RegexOptions.Multiline)]
    private static partial Regex StreamStart();

    [GeneratedRegex(@": Video: (.*)$")]
    private static partial Regex VideoStream();

    /// <summary>"1920x1080", not the "0x31637661" of a codec tag.</summary>
    [GeneratedRegex(@"\b(\d{1,5})x(\d{1,5})\b")]
    private static partial Regex Dimensions();

    /// <summary>"displaymatrix: rotation of -90.00 degrees" (ffmpeg 5+) or "rotate : 90" (older versions).</summary>
    [GeneratedRegex(@"(?:rotation of|rotate\s*:)\s*(-?\d+(?:\.\d+)?)")]
    private static partial Regex Rotation();

    /// <summary>The size written in the header of a JPEG, PNG, GIF, BMP or WebP image; null for other formats.</summary>
    internal static PixelSize? ReadImageHeader(Stream stream)
    {
        Span<byte> head = stackalloc byte[32];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..read];

        if (head.Length >= 4 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return ReadJpeg(stream);
        if (head.Length >= 24 && head[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return Checked(BinaryPrimitives.ReadInt32BigEndian(head[16..]), BinaryPrimitives.ReadInt32BigEndian(head[20..]));
        if (head.Length >= 10 && head[..3].SequenceEqual("GIF"u8))
            return Checked(BinaryPrimitives.ReadUInt16LittleEndian(head[6..]), BinaryPrimitives.ReadUInt16LittleEndian(head[8..]));
        if (head.Length >= 26 && head[..2].SequenceEqual("BM"u8))
        {
            // BITMAPCOREHEADER (12 bytes) has 16-bit sizes; the later headers 32-bit ones, negative heights for top-down rows.
            return BinaryPrimitives.ReadUInt32LittleEndian(head[14..]) == 12
                ? Checked(BinaryPrimitives.ReadUInt16LittleEndian(head[18..]), BinaryPrimitives.ReadUInt16LittleEndian(head[20..]))
                : Checked(BinaryPrimitives.ReadInt32LittleEndian(head[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(head[22..])));
        }

        if (head.Length >= 30 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return ReadWebP(head);
        return null;
    }

    private static PixelSize Checked(long width, long height) =>
        width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue ? new PixelSize((int)width, (int)height) : PixelSize.Unknown;

    private static PixelSize ReadWebP(ReadOnlySpan<byte> head)
    {
        var chunk = head[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
            return Checked(BinaryPrimitives.ReadUInt16LittleEndian(head[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(head[28..]) & 0x3FFF);
        if (chunk.SequenceEqual("VP8L"u8))
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(head[21..]);
            return Checked((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
            return Checked(Read24(head[24..]) + 1, Read24(head[27..]) + 1);
        return PixelSize.Unknown;

        static int Read24(ReadOnlySpan<byte> value) => value[0] | value[1] << 8 | value[2] << 16;
    }

    /// <summary>
    /// Walks the JPEG segments up to the frame header (SOF), reading the EXIF orientation on the way: a portrait
    /// photo is usually stored landscape with an orientation telling viewers to turn it.
    /// </summary>
    private static PixelSize ReadJpeg(Stream stream)
    {
        stream.Position = 2;
        var turned = false;
        Span<byte> marker = stackalloc byte[4];
        Span<byte> frame = stackalloc byte[5];
        while (stream.ReadAtLeast(marker, 4, throwOnEndOfStream: false) == 4)
        {
            if (marker[0] != 0xFF)
                return PixelSize.Unknown;
            var type = marker[1];
            if (type == 0xFF)
            {
                // Fill bytes before a marker.
                stream.Position -= 3;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(marker[2..]) - 2;
            if (length < 0)
                return PixelSize.Unknown;

            if (type is >= 0xC0 and <= 0xCF && type is not (0xC4 or 0xC8 or 0xCC))
            {
                if (stream.ReadAtLeast(frame, 5, throwOnEndOfStream: false) < 5)
                    return PixelSize.Unknown;
                var height = BinaryPrimitives.ReadUInt16BigEndian(frame[1..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(frame[3..]);
                return turned ? Checked(height, width) : Checked(width, height);
            }

            if (type == 0xE1 && length >= 14)
            {
                var segment = new byte[length];
                if (stream.ReadAtLeast(segment, length, throwOnEndOfStream: false) < length)
                    return PixelSize.Unknown;
                if (segment.AsSpan(0, 6).SequenceEqual("Exif\0\0"u8))
                    turned |= ExifOrientation(segment.AsSpan(6)) is >= 5 and <= 8;
                continue;
            }

            if (type is 0xD9 or 0xDA)
                return PixelSize.Unknown;
            stream.Position += length;
        }

        return PixelSize.Unknown;
    }

    /// <summary>Orientation tag (0x0112) of the first IFD of a TIFF block; 0 when missing. 5 to 8 swap the sides.</summary>
    internal static int ExifOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return 0;
        var little = tiff[0] == (byte)'I';
        if (!little && tiff[0] != (byte)'M')
            return 0;
        var ifd = little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);
        if (ifd > (uint)tiff.Length - 2)
            return 0;
        var count = UInt16(tiff, (int)ifd, little);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length)
                return 0;
            if (UInt16(tiff, entry, little) == 0x0112)
                return UInt16(tiff, entry + 8, little);
        }

        return 0;
    }

    private static int UInt16(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
}
