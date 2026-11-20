using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.Features;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The change feed (agents/docs/features/change-feed.md): server-sent events telling each open listing that a folder
/// it may see was changed through SFE (by another tab, another person, a move or copy job), so that it lists it again;
/// how keep alive goes (agents/docs/features/keep-alive.md), for the indicator of the top bar; and that the trash
/// changed, for the count of the sidebar's Trash link.
/// </summary>
public static class ChangeEndpoints
{
    /// <summary>
    /// The header naming the tab a request comes from, so that its own changes are not told back to it.
    /// </summary>
    public const string TabHeader = "X-SFE-Tab";

    /// <summary>Feeds one caller may keep open at once (one per tab).</summary>
    public const int MaxStreamsPerCaller = 12;

    /// <summary>A feed ends after this; the browser opens it again (reading the session and the rights anew).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// A `ping` event sent when nothing happened for this long: proxies keep the connection, and the client knows the
    /// server still answers (a comment would keep the connection too, but scripts don't see comments).
    /// </summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(25);

    /// <summary>The `ping` event (an event needs data to reach the page).</summary>
    public const string PingEvent = "event: ping\ndata: {}\n\n";

    /// <summary>The `trash` event: the client asks for its count again.</summary>
    public const string TrashEvent = "event: trash\ndata: {}\n\n";

    private static readonly ConcurrentDictionary<string, int> Streams = new();
    private static readonly Lock Gate = new();

    private static readonly string FolderChangesDescription = $"""
        Open with `EventSource`; `origin` is the `{TabHeader}` header of the request that changed it (a tab
        skips its own). `keepAlive` comes first, then each time keep alive's state changes: `none` (off, or
        nothing pinged), `ok`, `failing`; no folder nor error is named. `trash` (to callers with files.delete or
        files.deletePermanently) when something came into or left the trash: ask its count again. A `ping` event
        after {KeepAlive.TotalSeconds:N0} s without anything keeps it open; it ends after
        {Lifetime.TotalMinutes:N0} minutes and the browser opens it again. {MaxStreamsPerCaller} at once per
        caller (429).
        """;

    public static RouteGroupBuilder MapChangeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/files/changes", StreamAsync)
            .WithTags("Files")
            .RequireRight(Rights.Browse)
            .FolderScoped("only folders the caller sees (files.browse) are told")
            .WithName("FolderChanges")
            .WithSummary(
                "Server-sent events: `changed` with `{ path, origin }` (and `connection` for a connection's) each "
                    + "time a folder is changed through SFE; "
                    + "`keepAlive` with `{ state }`; `trash`; `ping`."
            )
            .WithDescription(FolderChangesDescription);
        return api;
    }

    private static async Task StreamAsync(
        HttpContext context,
        [AsParameters] ChangeFeedServices services,
        [AsParameters] ChangeFeedConnections connections
    )
    {
        var state = await services.Current.GetAsync();
        var caller = RequestLimits.Caller(context);
        if (!Enter(caller))
        {
            await TypedResults
                .Problem(
                    $"At most {MaxStreamsPerCaller} change feeds at once.",
                    statusCode: StatusCodes.Status429TooManyRequests
                )
                .ExecuteAsync(context);
            return;
        }

        using var queue = new ChangeFeedQueue(services, connections, state);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetime.CancelAfter(Lifetime);
        try
        {
            await WriteEventsAsync(context, queue, lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // The tab closed, the server stops, or the feed's time is over.
        }
        catch (IOException)
        {
            // The connection broke.
        }
        finally
        {
            Leave(caller);
        }
    }

    /// <summary>Sends the queue's events as they come, until the feed's time is over or the tab closes.</summary>
    private static async Task WriteEventsAsync(
        HttpContext context,
        ChangeFeedQueue queue,
        CancellationToken cancellationToken
    )
    {
        var response = context.Response;
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-store";
        // nginx would otherwise hold the events back.
        response.Headers["X-Accel-Buffering"] = "no";
        await response.WriteAsync("retry: 5000\n\n" + queue.KeepAliveNow, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await queue.NextEventsAsync(KeepAlive, cancellationToken) is not { } text)
                break;
            if (text.Length == 0)
                continue;
            await response.WriteAsync(text, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }

    private static bool Enter(string caller)
    {
        lock (Gate)
        {
            var open = Streams.GetValueOrDefault(caller);
            if (open >= MaxStreamsPerCaller)
                return false;
            Streams[caller] = open + 1;
            return true;
        }
    }

    private static void Leave(string caller)
    {
        lock (Gate)
        {
            if (Streams.GetValueOrDefault(caller) <= 1)
                Streams.TryRemove(caller, out _);
            else
                Streams[caller]--;
        }
    }
}
