namespace Sfe.Core.Data;

/// <summary>
/// The level of a <see cref="LogEntry"/>: <see cref="Microsoft.Extensions.Logging.LogLevel"/>'s numbers (stored as
/// they were before this enum), plus <see cref="SecurityWarning"/> for what someone did against the server's protections
/// (wrong passwords, lockouts, wrong setup codes, account recoveries): <see cref="Logs.SecurityLog"/>.
/// </summary>
public enum LogEntryLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,

    /// <summary>A warning logged with <see cref="Logs.SecurityLog.Event"/> (6 is <c>LogLevel.None</c>, never stored).</summary>
    SecurityWarning = 7,
}
