using Sfe.Core.Data;

namespace Sfe.Core.Logs;

/// <param name="TenantId">The library whose services logged it; null for the server's own.</param>
public sealed record LogEntryView(
    long Id,
    DateTimeOffset Timestamp,
    LogEntryLevel Level,
    string Category,
    string Message,
    string? Exception,
    int? TenantId
);
