using Microsoft.Extensions.Logging;
using Sfe.Core.Data;

namespace Sfe.Core.Logs;

/// <summary>
/// Security events: what someone did against the server's protections (wrong passwords, lockouts, wrong setup codes,
/// account recoveries, the installation changed). Logged as warnings with <see cref="Event"/>: the console shows them as
/// warnings, the server's log keeps them as <see cref="LogEntryLevel.SecurityWarning"/>, searchable apart.
/// </summary>
public static class SecurityLog
{
    public static readonly EventId Event = new(9000, "Security");

    /// <summary>A security warning: <see cref="LoggerExtensions.LogWarning(ILogger, EventId, string?, object?[])"/> with <see cref="Event"/>.</summary>
    public static void LogSecurityWarning(this ILogger logger, string message, params object?[] args) =>
#pragma warning disable CA2254 // The template is the caller's constant, passed through.
        logger.LogWarning(Event, message, args);
#pragma warning restore CA2254

    /// <summary>The level an entry is kept with.</summary>
    public static LogEntryLevel LevelOf(LogLevel level, EventId eventId) =>
        level == LogLevel.Warning && eventId.Id == Event.Id && eventId.Name == Event.Name ? LogEntryLevel.SecurityWarning : (LogEntryLevel)level;
}
