namespace Sfe.Core.Thumbnails;

/// <summary>
/// Which of ffmpeg's readers (demuxers) may open a file of the library. ffmpeg picks the reader from the content, not
/// the name: a file called <c>.mp4</c> may be a script naming other files or addresses (a playlist, a concatenation
/// list, an AviSynth or VapourSynth program). Every reader of real media stays allowed, so no format an install plays
/// today is lost; the ones that follow what the file says are left out.
/// </summary>
public static class FfmpegDemuxers
{
    /// <summary>
    /// Fewer readers than this in <c>ffmpeg -demuxers</c>: the list was not understood, nothing is restricted.
    /// </summary>
    private const int FewestExpected = 20;

    /// <summary>Characters of a line of <c>ffmpeg -demuxers</c> before the reader's names.</summary>
    private const int FlagsWidth = 4;

    /// <summary>
    /// Readers that open other files or addresses named in the file, run it as a program, or are devices.
    /// </summary>
    private static readonly HashSet<string> Refused = new(StringComparer.OrdinalIgnoreCase)
    {
        "concat",
        "hls",
        "applehttp",
        "dash",
        "webm_dash_manifest",
        "imf",
        "sdp",
        "rtp",
        "rtsp",
        "sap",
        "avisynth",
        "vapoursynth",
        "lavfi",
        "dshow",
        "gdigrab",
        "vfwcap",
        "x11grab",
        "xcbgrab",
        "kmsgrab",
        "fbdev",
        "v4l2",
        "video4linux2",
        "alsa",
        "pulse",
        "oss",
        "sndio",
        "jack",
        "openal",
        "avfoundation",
        "iec61883",
        "libdc1394",
        "libcdio",
        "decklink",
        "android_camera",
        "bktr",
    };

    /// <summary>
    /// The value of ffmpeg's <c>-format_whitelist</c> from the output of <c>ffmpeg -demuxers</c>: its readers without
    /// the <see cref="Refused"/> ones and the devices, separated by commas; null when the output is not such a list
    /// (an ffmpeg too old to have the option: nothing is restricted then).
    /// </summary>
    public static string? AllowedList(string demuxersOutput)
    {
        var allowed = demuxersOutput
            .Split('\n')
            .SkipWhile(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal))
            .Skip(1)
            .Select(NamesOf)
            .OfType<string>()
            .SelectMany(names => names.Split(','))
            .Where(name => !Refused.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return allowed.Count >= FewestExpected ? string.Join(',', allowed) : null;
    }

    /// <summary>
    /// The names on a line as <c> D   mov,mp4,m4a   QuickTime / MOV</c> (flags, names, description); null for a line
    /// that is not a reader, for a device (flag <c>d</c>), or when one of its names is refused.
    /// </summary>
    private static string? NamesOf(string line)
    {
        if (line.Length <= FlagsWidth)
            return null;
        // The flags are a column of their own (" D  " before ffmpeg 7, " D d" since): the names start after it.
        var flags = line[..FlagsWidth];
        var names = line[FlagsWidth..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (names is null || !flags.Contains('D') || flags.Contains('d'))
            return null;
        return names.Split(',').Any(Refused.Contains) ? null : names;
    }
}
