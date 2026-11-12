namespace Sfe.Core.Options;

/// <summary>
/// Keep alive: touches the library's folder every <see cref="IntervalSeconds"/>, so that a file server never sees the
/// session to its share go idle and drops it (Windows' <c>autodisconnect</c>, a NAS's idle limit).
/// </summary>
public sealed class KeepAliveOptions
{
    public const int MinIntervalSeconds = 10;
    public const int MaxIntervalSeconds = 3600;
    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutSeconds = 300;

    /// <summary>Off by default: an install upgraded on a share changes nothing until it is turned on.</summary>
    public KeepAliveMode Mode { get; set; } = KeepAliveMode.Off;

    /// <summary>Seconds between two pings of a folder (10–3600); shorter than the file server's idle limit.</summary>
    public int IntervalSeconds { get; set; } = 240;

    /// <summary>A ping taking longer counts as failed (1–300 s); the next one waits until it ends.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// More library folders to keep alive besides the root (relative to it, separated by <c>/</c>): folders reached
    /// through a link to another share. Each one is looked at on its own by <see cref="KeepAliveMode.Auto"/>.
    /// </summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>How the library folder is pinged (more folders are always read).</summary>
    public KeepAliveMethod Method { get; set; } = KeepAliveMethod.List;
}
