using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The change feed (agents/docs/features/change-feed.md): server-sent events telling each open listing that a folder it
/// may see was changed through SFE (by another tab, another person, a move or copy job), so that it lists it again; how
/// keep alive goes (agents/docs/features/keep-alive.md), for the indicator of the top bar; and that the trash changed,
/// for the count of the sidebar's Trash link; how far the items being written in a folder are, for its listing; that a
/// folder a tab shows was changed outside SFE (watched only while a tab shows it: <c>PUT files/changes/watch</c>).
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

    private static readonly string FolderChangesDescription = $$"""
        Open with `EventSource`; `origin` is the `{{TabHeader}}` header of the request that changed it (a tab
        skips its own). `keepAlive` comes first, then each time keep alive's state changes: `none` (off, or
        nothing pinged), `ok`, `failing`; no folder nor error is named. `trash` (to callers with files.delete or
        files.deletePermanently) when something came into or left the trash: ask its count again. `writing` with
        `{ path, items: [{ name, finalName, bytesDone, bytesTotal }] }` when items being written in a folder the
        caller sees moved on, started or ended (at most every {{ItemsBeingWritten.Interval.TotalMilliseconds:N0}} ms
        per folder, and every {{ItemsBeingWritten.Heartbeat.TotalSeconds:N0}} s while some are; `name` ends with
        `.sfe-partial`, `bytesTotal` 0 when unknown, `items` empty once the last one ended). `changed` with
        `outside` true: a folder changed outside SFE (another program), told only for folders some tab watches
        (`PUT files/changes/watch`, naming the feed's `tab`). `task` with `{ id, task, path, outcome, summary,
        finished, mine }` when a background task's run ended: to the account that started it (`mine`), and failed
        runs to callers with tasks.view; transfers are not told. `since` (the latest `finished` seen) also sends
        those that ended since then, among the last {{TaskEndings.Kept}} the server keeps in memory. A `ping` event
        after {{KeepAlive.TotalSeconds:N0}} s without anything keeps it open; it ends after
        {{Lifetime.TotalMinutes:N0}} minutes and the browser opens it again. {{MaxStreamsPerCaller}} at once per
        caller (429).
        """;

    private const string WatchDescription = $$"""
        The `{{TabHeader}}` header names the tab, whose feed was opened with `?tab=` the same; `path` is the
        folder it shows (`""` the top of the library), `null` none. The folder is followed until the tab names
        another, or its feed closes (a feed opened again needs it named again). Changes are then told as
        `changed` with `outside` true to every feed whose caller sees the folder. `watching` false: no feed of
        this tab is open in this library, or the folder is missing or hidden.
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
                    + "`keepAlive` with `{ state }`; `trash`; `writing` with `{ path, items }`; `task`; `ping`."
            )
            .WithDescription(FolderChangesDescription);

        api.MapPut("/files/changes/watch", WatchAsync)
            .WithTags("Files")
            .RequireRight(Rights.Browse)
            .FolderScoped("a folder hidden from the caller is not followed")
            .WithName("WatchFolder")
            .WithSummary("Follows the folder a tab shows for changes made outside SFE, while the tab's feed is open.")
            .WithDescription(WatchDescription);
        return api;
    }

    private static async Task StreamAsync(
        HttpContext context,
        [AsParameters] ChangeFeedServices services,
        [AsParameters] ChangeFeedConnections connections,
        string? tab = null,
        DateTimeOffset? since = null
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
        queue.TellEndedSince(since);
        var named = ChangeFeedTabs.IsValid(tab);
        if (named)
            ChangeFeedTabs.Add(caller, tab!, queue);
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
            if (named)
                ChangeFeedTabs.Remove(caller, tab!, queue);
            Leave(caller);
        }
    }

    private static async Task<Ok<FolderWatchAnswer>> WatchAsync(
        HttpContext context,
        FolderWatchRequest request,
        CurrentAccess current
    )
    {
        var state = await current.GetAsync();
        var tab = context.Request.Headers[TabHeader].ToString();
        if (ChangeFeedTabs.Find(RequestLimits.Caller(context), tab) is not { } feed || feed.TenantId != state.TenantId)
            return TypedResults.Ok(new FolderWatchAnswer(false));
        if (request.Connection is { } connection)
        {
            var path = request.Path is null ? null : RemotePaths.Normalize(request.Path);
            var followed = path is null
                ? feed.Watch(null)
                : await feed.WatchAsync(connection, path, context.RequestAborted);
            return TypedResults.Ok(new FolderWatchAnswer(path is not null && followed));
        }
        // A path leaving the library (`..`) is no folder: nothing followed.
        var folder = LibraryPaths.NormalizeRelative(request.Path ?? "..");
        return TypedResults.Ok(new FolderWatchAnswer(feed.Watch(folder)));
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
