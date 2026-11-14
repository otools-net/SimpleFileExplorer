using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;

namespace Sfe.Api;

/// <summary>
/// Tells the server log who still calls the unversioned paths (<see cref="LegacyApiPaths"/>), so that an
/// administrator knows which clients to update before SFE 2.0: one line per caller (account, or address without one)
/// and day, not one per request. Remembers today's callers only, at most <see cref="MaxCallersPerDay"/>.
/// </summary>
public sealed class LegacyPathLog(TimeProvider time, ILogger<LegacyPathLog> logger)
{
    /// <summary>
    /// The callers remembered in a day; past it, one last line says so and the rest of the day is quiet.
    /// </summary>
    public const int MaxCallersPerDay = 10_000;

    private const int MaxAgentLength = 120;

    private readonly Lock _gate = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private DateOnly _day;
    private bool _full;

    private enum Verdict
    {
        First,
        Seen,
        Full,
    }

    /// <summary>A request came to <paramref name="legacyPath"/>: logged when it is its caller's first today.</summary>
    public void Note(HttpContext context, PathString legacyPath)
    {
        var caller = CallerOf(context);
        switch (Admit(caller))
        {
            case Verdict.First:
                logger.LogWarning(
                    "{Caller} called the API at an unversioned path ({Method} {Group}…, {Agent}): it answers until "
                        + "SFE 2.0, clients should use /api/v1 (logged once a day per caller).",
                    caller,
                    context.Request.Method,
                    GroupOf(legacyPath),
                    AgentOf(context.Request)
                );
                break;
            case Verdict.Full:
                logger.LogWarning(
                    "More than {Count} callers used unversioned API paths today: no more lines about them until "
                        + "tomorrow.",
                    MaxCallersPerDay
                );
                break;
        }
    }

    private Verdict Admit(string caller)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        lock (_gate)
        {
            if (today != _day)
            {
                _day = today;
                _seen.Clear();
                _full = false;
            }

            if (_seen.Contains(caller))
                return Verdict.Seen;
            if (_seen.Count < MaxCallersPerDay)
            {
                _seen.Add(caller);
                return Verdict.First;
            }

            if (_full)
                return Verdict.Seen;
            _full = true;
            return Verdict.Full;
        }
    }

    private static string CallerOf(HttpContext context) =>
        context.User.UserId() is { } user
            ? $"Account {user}"
            : $"Address {context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    // The first two segments (/api/files/shared): the rest may be a link's token, which the log must not keep.
    private static string GroupOf(PathString path)
    {
        var segments = path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "/" + string.Join('/', segments.Take(3));
    }

    private static string AgentOf(HttpRequest request)
    {
        var agent = request.Headers[HeaderNames.UserAgent].ToString();
        if (agent.Length == 0)
            return "no user agent";
        return agent.Length > MaxAgentLength ? agent[..MaxAgentLength] + "…" : agent;
    }
}
