namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// The server's log (Administration › Server › Logs): what every part of SFE logs, at the levels of the <c>Logging</c>
/// section, kept in the accounts' database and, when <see cref="WriteFiles"/>, also in daily files.
/// </summary>
public sealed class LogOptions
{
    /// <summary>Days the log entries (and the daily files) are kept; 0 = forever.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Also writes each entry to a file of the day (<c>sfe-yyyyMMdd.log</c>, UTC) in <see cref="Directory"/>.
    /// </summary>
    public bool WriteFiles { get; set; }

    /// <summary>
    /// The folder of the daily files, next to the database and the thumbnails by default. Only the configuration sets
    /// it (it decides where data lives). Relative paths are resolved against the content root; a leading <c>~</c> is
    /// the home folder.
    /// </summary>
    public string Directory { get; set; } = "data/logs";
}
