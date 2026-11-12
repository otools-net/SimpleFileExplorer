using Sfe.Core.Data;

namespace Sfe.Core.Logs;

/// <summary>A search of the log. Every filter is optional.</summary>
/// <param name="Text">Found in the message, the logger's name or the exception (ignoring case).</param>
/// <param name="Levels">Only these levels.</param>
/// <param name="From">From this moment on.</param>
/// <param name="To">Before this moment.</param>
/// <param name="Tenant">Only the entries of this library; 0 = only the server's own (no library).</param>
/// <param name="Before">The <see cref="LogPage.Next"/> of the previous page.</param>
public sealed record LogQuery(
    string? Text = null,
    IReadOnlyCollection<LogEntryLevel>? Levels = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? Tenant = null,
    long? Before = null,
    int? Limit = null
);
