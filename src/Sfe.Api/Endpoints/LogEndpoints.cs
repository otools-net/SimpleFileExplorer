using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Logs;

namespace Otools.Net.Sfe.Api.Endpoints;

public static class LogEndpoints
{
    private static readonly string SearchLogsDescription = $"""
        Filters, all optional: `q` (text found in the message, the logger's name or the exception, ignoring
        case), `levels` (comma-separated: `trace`, `debug`, `information`, `warning`, `securityWarning`,
        `error`, `critical`; `securityWarning` = security events: wrong passwords, lockouts, wrong setup codes,
        recoveries), `from` and `to` (ISO 8601 moments; `to` excluded), `tenant` (a space's id; `0` = the
        server's own entries, outside any space). Server administrators see every space's entries; others only
        those of their space, whatever `tenant` says. Pages of `limit` entries (default
        {AuditLogService.DefaultPageSize}, at most {AuditLogService.MaxPageSize}); pass `next` as `before` for
        the following page. The last seconds' entries may not be there yet: the log is written every few
        seconds.
        """;

    public static RouteGroupBuilder MapLogEndpoints(this RouteGroupBuilder api)
    {
        var logs = api.MapGroup("/logs").WithTags("Logs");

        logs.MapGet("/", Search)
            .RequireRight(Rights.ViewLogs)
            .WithName("SearchLogs")
            .WithSummary("Entries of the server's log, newest first: warnings, errors and messages.")
            .WithDescription(SearchLogsDescription);

        return api;
    }

    private static async Task<Results<Ok<LogSearchResult>, ValidationProblem>> Search(
        [AsParameters] LogServices services,
        [AsParameters] LogFilter filter,
        CancellationToken cancellationToken,
        long? before = null,
        int? limit = null
    )
    {
        var (log, current) = services;
        var (q, levels, from, to, tenant) = filter;
        if (!TryParseLevels(levels, out var parsed))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["levels"] =
                    [
                        "Use trace, debug, information, warning, securityWarning, error or critical, "
                            + "separated by commas.",
                    ],
                }
            );
        }

        var state = await current.GetAsync();
        var all = state.Access.IsServerAdmin;
        var page = await log.SearchAsync(
            new LogQuery(q, parsed, from, to, all ? tenant : state.TenantId, before, limit),
            cancellationToken
        );
        var options = log.Options();
        return TypedResults.Ok(
            new LogSearchResult(
                page.Entries,
                page.Next,
                options.RetentionDays,
                options.WriteFiles,
                all ? log.Directory : null,
                all
            )
        );
    }

    private static bool TryParseLevels(string? text, out List<LogEntryLevel> levels)
    {
        levels = [];
        foreach (
            var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        )
        {
            if (
                !Enum.TryParse<LogEntryLevel>(part, ignoreCase: true, out var level)
                || !Enum.IsDefined(level)
                || char.IsDigit(part[0])
            )
            {
                return false;
            }

            levels.Add(level);
        }

        return true;
    }
}
