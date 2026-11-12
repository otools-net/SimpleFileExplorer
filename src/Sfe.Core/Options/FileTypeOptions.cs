namespace Sfe.Core.Options;

/// <remarks>
/// The lists start empty and get their defaults from <see cref="ApplyDefaults"/> when not configured: the
/// configuration binder adds configured items to a list's initial items instead of replacing them.
/// </remarks>
public sealed class FileTypeOptions
{
    public static readonly string[] DefaultImages = ["bmp", "gif", "jpeg", "jpg", "png", "tif", "tiff", "webp", "avif"];
    public static readonly string[] DefaultVideos =
    [
        "3gp",
        "avi",
        "flv",
        "m4v",
        "mkv",
        "mov",
        "mp4",
        "mpeg",
        "mpg",
        "ts",
        "webm",
        "wmv",
    ];
    public static readonly string[] DefaultAudio = ["aac", "flac", "m4a", "mp3", "ogg", "opus", "wav"];

    public List<string> Images { get; set; } = [];
    public List<string> Videos { get; set; } = [];
    public List<string> Audio { get; set; } = [];

    /// <summary>
    /// More extensions the web client shows as text, besides the ones it knows (<c>.txt</c>, <c>.log</c>, <c>.json</c>,
    /// code...): not media, so not in the other lists. Without a dot, lower case.
    /// </summary>
    public List<string> Text { get; set; } = [];

    /// <param name="isConfigured">Whether the configuration has a value for the named list.</param>
    public void ApplyDefaults(Func<string, bool> isConfigured)
    {
        if (!isConfigured(nameof(Images)))
            Images = [.. DefaultImages];
        if (!isConfigured(nameof(Videos)))
            Videos = [.. DefaultVideos];
        if (!isConfigured(nameof(Audio)))
            Audio = [.. DefaultAudio];
    }
}
