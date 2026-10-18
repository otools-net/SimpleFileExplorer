namespace Sfe.Core.Data;

/// <summary>
/// One entry of the server's log (<see cref="Logs.AuditLogService"/>): what a logger wrote, when, and in which library.
/// Kept in the accounts' database, for the whole server, until <see cref="Options.LogOptions.RetentionDays"/>.
/// </summary>
public sealed class LogEntry
{
    public long Id { get; set; }

    /// <summary>UTC.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Stored as its number (<see cref="Microsoft.Extensions.Logging.LogLevel"/>'s, plus <see cref="LogEntryLevel.SecurityWarning"/>).</summary>
    public LogEntryLevel Level { get; set; }

    /// <summary>The logger's name, e.g. <c>Sfe.Core.Indexing.ScanCoordinator</c>.</summary>
    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>The exception logged with the entry (type, message and stack), if any.</summary>
    public string? Exception { get; set; }

    /// <summary>The library whose services logged it; null for the server's own (requests, start-up, accounts).</summary>
    public int? TenantId { get; set; }
}
