using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.KeepAlive;
using Sfe.Core.Library;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Path">The folder changed (root-relative).</param>
/// <param name="Origin">The tab whose request changed it (`X-SFE-Tab`), which needs no notice; null from a background job.</param>
public sealed record FolderChange(string Path, string? Origin);

/// <param name="State">How keep alive goes in the space: `none` (off, or nothing pinged), `ok`, `failing`.</param>
public sealed record KeepAliveSignal(KeepAliveState State);

/// <summary>
/// The change feed (agents/docs/features/change-feed.md): server-sent events telling each open listing that a folder
/// it may see was changed through SFE (by another tab, another person, a move or copy job), so that it lists it again;
/// and how keep alive goes (agents/docs/features/keep-alive.md), for the indicator of the top bar.
/// </summary>
public static class ChangeEndpoints
{
    /// <summary>The header naming the tab a request comes from, so that its own changes are not told back to it.</summary>
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

    private static readonly ConcurrentDictionary<string, int> Streams = new();
    private static readonly Lock Gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static RouteGroupBuilder MapChangeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/files/changes", StreamAsync)
            .WithTags("Files")
            .RequireRight(Rights.Browse)
            .FolderScoped("only folders the caller sees (files.browse) are told")
            .WithName("FolderChanges")
            .WithSummary("Server-sent events: `changed` with `{ path, origin }` each time a folder is changed through SFE; `keepAlive` with `{ state }`; `ping`.")
            .WithDescription($"""
                Open with `EventSource`; `origin` is the `{TabHeader}` header of the request that changed it (a tab skips
                its own). `keepAlive` comes first, then each time keep alive's state changes: `none` (off, or nothing
                pinged), `ok`, `failing`; no folder nor error is named. A `ping` event after {KeepAlive.TotalSeconds:N0} s without anything keeps it open; it ends after
                {Lifetime.TotalMinutes:N0} minutes and the browser opens it again. {MaxStreamsPerCaller} at once per caller (429).
                """);
        return api;
    }

    private static async Task StreamAsync(
        HttpContext context, LibraryBrowser browser, KeepAliveService keepAlive, LibraryPaths paths, CurrentAccess current, IHttpContextAccessor accessor)
    {
        var state = await current.GetAsync();
        var caller = RequestLimits.Caller(context);
        if (!Enter(caller))
        {
            await TypedResults.Problem($"At most {MaxStreamsPerCaller} change feeds at once.", statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(context);
            return;
        }

        // Read by the request (or job) that changed a folder, or by keep alive's loop: only queued here (a FolderChange
        // or a KeepAliveSignal), written by this request's own loop.
        var changes = Channel.CreateBounded<object>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        void OnChanged(string path)
        {
            var origin = accessor.HttpContext?.Request.Headers[TabHeader].ToString();
            changes.Writer.TryWrite(new FolderChange(path, string.IsNullOrEmpty(origin) ? null : origin));
        }

        void OnKeepAlive(KeepAliveState keepAliveState) => changes.Writer.TryWrite(new KeepAliveSignal(keepAliveState));

        browser.FolderChanged += OnChanged;
        keepAlive.StateChanged += OnKeepAlive;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        lifetime.CancelAfter(Lifetime);
        var token = lifetime.Token;
        try
        {
            var response = context.Response;
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-store";
            // nginx would otherwise hold the events back.
            response.Headers["X-Accel-Buffering"] = "no";
            await response.WriteAsync("retry: 5000\n\n" + KeepAliveEvent(new KeepAliveSignal(keepAlive.State)), token);
            await response.Body.FlushAsync(token);
            while (!token.IsCancellationRequested)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                wait.CancelAfter(KeepAlive);
                bool ready;
                try
                {
                    ready = await changes.Reader.WaitToReadAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    await response.WriteAsync(PingEvent, token);
                    await response.Body.FlushAsync(token);
                    continue;
                }
                if (!ready)
                    break;
                // Several at once (a move touches both folders): one write.
                var text = new System.Text.StringBuilder();
                while (changes.Reader.TryRead(out var item))
                {
                    if (item is KeepAliveSignal signal)
                        text.Append(KeepAliveEvent(signal));
                    else if (item is FolderChange change && state.Check(paths, change.Path, Rights.Browse) == PathVerdict.Allowed)
                        text.Append("event: changed\ndata: ").Append(JsonSerializer.Serialize(change, Json)).Append("\n\n");
                }
                if (text.Length == 0)
                    continue;
                await response.WriteAsync(text.ToString(), token);
                await response.Body.FlushAsync(token);
            }
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
            browser.FolderChanged -= OnChanged;
            keepAlive.StateChanged -= OnKeepAlive;
            Leave(caller);
        }
    }

    private static string KeepAliveEvent(KeepAliveSignal signal) => $"event: keepAlive\ndata: {JsonSerializer.Serialize(signal, Json)}\n\n";

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
