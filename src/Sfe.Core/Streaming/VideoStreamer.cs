using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Thumbnails;

namespace Sfe.Core.Streaming;

/// <summary>What ffmpeg tells about a video, to convert it.</summary>
/// <param name="Duration">Seconds; null when the container doesn't say.</param>
/// <param name="VideoCodec">The first picture stream's codec (<c>h264</c>, <c>hevc</c>...), null without pictures.</param>
/// <param name="BrowserPictures">That stream is H.264 in a pixel format browsers play (8-bit 4:2:0), so it can be copied.</param>
/// <param name="AudioCodec">The first sound stream's codec (<c>aac</c>, <c>ac3</c>...), null without sound.</param>
public sealed record VideoProbe(double? Duration, string? VideoCodec, bool BrowserPictures, string? AudioCodec);

/// <summary>A video ready to be converted (<see cref="VideoStreamer.TryBegin"/>): run it with <see cref="VideoStreamer.RunAsync"/>.</summary>
public sealed record VideoStream(string FullPath, double Start, bool Transcoded, VideoStreams.Lease Lease);

/// <summary>
/// Plays videos through ffmpeg for browsers that cannot play the file (<see cref="StreamingOptions"/>): H.264 and AAC in
/// fragmented MP4, written while it is made, from the position asked for. The response has no length and no ranges:
/// the player seeks by asking again from another position.
/// </summary>
public sealed partial class VideoStreamer(
    LibraryPaths paths, MediaClassifier classifier, Ffmpeg ffmpeg, MediaDimensions media, VideoStreams streams,
    IOptionsMonitor<SfeOptions> options, ILogger<VideoStreamer> logger)
{
    /// <summary>Probes kept, by file: a player asks for each video once, then again at every seek.</summary>
    private const int ProbeCacheSize = 256;

    private readonly ConcurrentDictionary<string, (long Size, DateTime Modified, VideoProbe Probe)> _probes = new(LibraryPaths.PathComparer);

    /// <summary>ffmpeg was found and streaming isn't turned off.</summary>
    public bool IsAvailable => Mode != StreamingMode.Off && ffmpeg.IsAvailable;

    public StreamingMode Mode => options.CurrentValue.Streaming.Mode;

    /// <summary>Describes the video at <paramref name="path"/> (relative to the root); null when it is not a video of the library.</summary>
    public async Task<VideoProbe?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsAvailable || !Resolve(path, out var fullPath))
            return null;

        var file = new FileInfo(fullPath);
        if (_probes.TryGetValue(fullPath, out var cached) && cached.Size == file.Length && cached.Modified == file.LastWriteTimeUtc)
            return cached.Probe;

        var probe = Parse(await media.ProbeAsync(fullPath, cancellationToken));
        if (_probes.Count >= ProbeCacheSize)
            _probes.Clear();
        _probes[fullPath] = (file.Length, file.LastWriteTimeUtc, probe);
        return probe;
    }

    /// <summary>
    /// Registers the stream of <paramref name="path"/> from <paramref name="start"/> seconds for <paramref name="viewer"/>.
    /// </summary>
    /// <param name="client">The player asking (it replaces its own earlier stream), when it says.</param>
    /// <returns>Null when the server already converts <see cref="StreamingOptions.MaxStreams"/> videos.</returns>
    public VideoStream? TryBegin(string path, VideoProbe probe, double start, StreamViewer viewer, string? client)
    {
        if (!Resolve(path, out var fullPath))
            return null;

        var settings = options.CurrentValue.Streaming;
        var transcoded = !(settings.CopyVideo && probe.BrowserPictures);
        start = Math.Max(0, probe.Duration is { } duration ? Math.Min(start, duration) : start);
        var lease = streams.TryBegin(client, settings.MaxStreams, LibraryPaths.NormalizeRelative(path) ?? path, viewer, start, transcoded);
        return lease is null ? null : new VideoStream(fullPath, start, transcoded, lease);
    }

    /// <summary>
    /// Converts the video into <paramref name="output"/> until it ends, the viewer leaves (<paramref name="cancellationToken"/>)
    /// or the same player asks for another position. Disposes of the stream's registration.
    /// </summary>
    public async Task RunAsync(VideoStream stream, VideoProbe probe, Stream output, CancellationToken cancellationToken)
    {
        using var lease = stream.Lease;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Stopping);
        var arguments = Arguments(stream.FullPath, stream.Start, probe, stream.Transcoded, options.CurrentValue.Streaming);
        try
        {
            // No time limit: a film plays for hours. The viewer leaving ends it.
            var (exitCode, errors) = await ffmpeg.RunAsync(arguments, Timeout.InfiniteTimeSpan, new CountingStream(output, lease), stop.Token);
            if (exitCode != 0)
                logger.LogWarning("ffmpeg could not convert {File} (exit code {ExitCode}): {Errors}", stream.FullPath, exitCode, errors.Trim());
        }
        catch (OperationCanceledException)
        {
            // The viewer left or seeked.
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "The stream of {File} ended early", stream.FullPath);
        }
    }

    /// <summary>The ffmpeg command line converting <paramref name="fullPath"/> from <paramref name="start"/> seconds to standard output.</summary>
    internal static List<string> Arguments(string fullPath, double start, VideoProbe probe, bool transcoded, StreamingOptions settings)
    {
        List<string> arguments = ["-hide_banner", "-nostdin", "-loglevel", "error"];
        // Before the input: a fast seek (to the key frame before when copying, exact when encoding).
        if (start > 0)
            arguments.AddRange(["-ss", start.ToString("0.###", CultureInfo.InvariantCulture)]);
        arguments.AddRange(["-i", fullPath, "-map", "0:v:0?", "-map", "0:a:0?", "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1"]);

        if (probe.VideoCodec is null)
        {
            arguments.Add("-vn");
        }
        else if (transcoded)
        {
            // Even sides (4:2:0 needs them), no taller than MaxHeight; a key frame every 2 s starts a fragment, so
            // that the player gets its first pictures soon.
            var height = settings.MaxHeight > 0 ? $"min({settings.MaxHeight}\\,trunc(ih/2)*2)" : "trunc(ih/2)*2";
            arguments.AddRange([
                "-vf", $"scale=-2:{height}", "-c:v", "libx264", "-preset", "veryfast",
                "-crf", Math.Clamp(settings.Quality, 0, 51).ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", "yuv420p", "-force_key_frames", "expr:gte(t,n_forced*2)",
            ]);
        }
        else
        {
            arguments.AddRange(["-c:v", "copy"]);
        }

        arguments.AddRange(probe.AudioCodec == "aac" ? ["-c:a", "copy"] : ["-c:a", "aac", "-ac", "2", "-b:a", "192k"]);
        arguments.AddRange(["-movflags", "frag_keyframe+empty_moov+default_base_moof", "-f", "mp4", "pipe:1"]);
        return arguments;
    }

    /// <summary>Reads <see cref="MediaDimensions.ProbeAsync"/>'s output: duration, then the first picture and sound streams.</summary>
    internal static VideoProbe Parse(string output)
    {
        var duration = MediaDimensions.ParseDuration(output);
        string? video = null, audio = null;
        var browserPictures = false;
        foreach (Match stream in StreamLine().Matches(output))
        {
            var (kind, codec, details) = (stream.Groups["kind"].Value, stream.Groups["codec"].Value, stream.Groups["details"].Value);
            if (kind == "Video" && video is null && !details.Contains("(attached pic)", StringComparison.Ordinal))
            {
                video = codec;
                browserPictures = codec == "h264" && BrowserPixelFormat().IsMatch(details);
            }
            else if (kind == "Audio")
            {
                audio ??= codec;
            }
        }

        return new VideoProbe(duration > 0 ? duration : null, video, browserPictures, audio);
    }

    [GeneratedRegex(@"^\s*Stream #.*?: (?<kind>Video|Audio): (?<codec>\w+)(?<details>.*)$", RegexOptions.Multiline)]
    private static partial Regex StreamLine();

    /// <summary>8-bit 4:2:0 (<c>yuv420p</c>, full-range <c>yuvj420p</c>), not <c>yuv420p10le</c>.</summary>
    [GeneratedRegex(@"\byuvj?420p\b")]
    private static partial Regex BrowserPixelFormat();

    private bool Resolve(string path, out string fullPath)
    {
        fullPath = string.Empty;
        if (!paths.TryResolve(path, out var resolved) || !File.Exists(resolved) || classifier.Classify(Path.GetExtension(resolved)) != ItemKind.Video)
            return false;
        fullPath = resolved;
        return true;
    }

    /// <summary>Forwards ffmpeg's output to the viewer, counting it for the list of streams.</summary>
    private sealed class CountingStream(Stream inner, VideoStreams.Lease lease) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            lease.Sent(buffer.Length);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            lease.Sent(count);
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
