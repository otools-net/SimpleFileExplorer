using Microsoft.AspNetCore.Mvc;
using Sfe.Core.Logs;

namespace Sfe.Api.Endpoints;

/// <param name="Entries">Newest first.</param>
/// <param name="Next">Pass as <c>before</c> for the next (older) page; null on the last one.</param>
/// <param name="RetentionDays">Days entries are kept; 0 = forever.</param>
/// <param name="WriteFiles">Entries are also written to daily files.</param>
/// <param name="FilesDirectory">Where those files are (server administrators only).</param>
/// <param name="AllSpaces">The caller sees the whole server's log (a server administrator); otherwise only its
/// space's.</param>
public sealed record LogSearchResult(
    IReadOnlyList<LogEntryView> Entries,
    long? Next,
    int RetentionDays,
    bool WriteFiles,
    string? FilesDirectory,
    bool AllSpaces
);

/// <summary>
/// The filters of a log search, from the query (bound with <c>[AsParameters]</c>; <c>before</c> and <c>limit</c> stay
/// parameters of the handler, in this order after them).
/// </summary>
public sealed record LogFilter(
    [FromQuery(Name = "q")] string? Q = null,
    [FromQuery(Name = "levels")] string? Levels = null,
    [FromQuery(Name = "from")] DateTimeOffset? From = null,
    [FromQuery(Name = "to")] DateTimeOffset? To = null,
    [FromQuery(Name = "tenant")] int? Tenant = null
);
