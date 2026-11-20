using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.KeepAlive;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What one change feed listens to while it is open: folders changed in the library and in connections, keep alive's
/// state, the trash (for callers who may open it). The events are only queued by the request (or job, or keep alive's
/// loop) that raised them; the feed's own loop takes them as the text of server-sent events, keeping those the caller
/// may see. Disposing it stops listening.
/// </summary>
internal sealed class ChangeFeedQueue : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ChangeFeedServices _services;
    private readonly AccessState _state;
    private readonly ConnectionFeed _connections;

    // Holds a FolderChange, a ConnectionFolderChange, a KeepAliveSignal or a TrashSignal.
    private readonly Channel<object> _changes = Channel.CreateBounded<object>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true }
    );

    public ChangeFeedQueue(ChangeFeedServices services, ChangeFeedConnections connections, AccessState state)
    {
        (_services, _state) = (services, state);
        services.Browser.FolderChanged += OnChanged;
        _connections = new ConnectionFeed(connections, state, _changes.Writer, services.Accessor);
        services.KeepAlive.StateChanged += OnKeepAlive;
        // The trash's count is for those who may open it.
        if (state.Access.Rights.Contains(Rights.DeleteFiles) || state.Access.Rights.Contains(Rights.DeletePermanently))
            services.Trash.EntriesChanged += OnTrash;
    }

    /// <summary>The `keepAlive` event with keep alive's state now (a feed's first event).</summary>
    public string KeepAliveNow => KeepAliveEvent(new KeepAliveSignal(_services.KeepAlive.State));

    /// <summary>
    /// The events queued next, as text: waits for one, then takes all those queued with it (a move changes two
    /// folders) in one text, with one `trash` at most (emptying the trash purges many). Empty when the caller may see
    /// none of them; the `ping` event when none came for <paramref name="quiet"/>; null when the queue ended.
    /// </summary>
    public async Task<string?> NextEventsAsync(TimeSpan quiet, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(quiet);
        try
        {
            if (!await _changes.Reader.WaitToReadAsync(wait.Token))
                return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ChangeEndpoints.PingEvent;
        }
        return await TakeQueuedAsync(cancellationToken);
    }

    public void Dispose()
    {
        _services.Browser.FolderChanged -= OnChanged;
        _services.KeepAlive.StateChanged -= OnKeepAlive;
        _services.Trash.EntriesChanged -= OnTrash;
        _connections.Dispose();
    }

    private async Task<string> TakeQueuedAsync(CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var trashChanged = false;
        while (_changes.Reader.TryRead(out var item))
        {
            if (item is TrashSignal)
                trashChanged = true;
            else if (await EventOfAsync(item, cancellationToken) is { } seen)
                text.Append(seen);
        }
        if (trashChanged)
            text.Append(ChangeEndpoints.TrashEvent);
        return text.ToString();
    }

    /// <summary>The event telling <paramref name="item"/>; null when the caller may not see it.</summary>
    private async Task<string?> EventOfAsync(object item, CancellationToken cancellationToken)
    {
        if (item is KeepAliveSignal signal)
            return KeepAliveEvent(signal);
        if (item is FolderChange change)
        {
            return _state.Check(_services.Paths, change.Path, Rights.Browse) == PathVerdict.Allowed
                ? ChangedEvent(change)
                : null;
        }
        if (item is ConnectionFolderChange remote && await _connections.SeesAsync(remote, cancellationToken))
            return ChangedEvent(remote);
        return null;
    }

    private void OnChanged(string path)
    {
        var origin = _services.Accessor.HttpContext?.Request.Headers[ChangeEndpoints.TabHeader].ToString();
        _changes.Writer.TryWrite(new FolderChange(path, string.IsNullOrEmpty(origin) ? null : origin));
    }

    private void OnKeepAlive(KeepAliveState keepAliveState) =>
        _changes.Writer.TryWrite(new KeepAliveSignal(keepAliveState));

    private void OnTrash() => _changes.Writer.TryWrite(new TrashSignal());

    /// <summary>`changed`, with a library folder (`path`, `origin`) or a connection's (and its `connection`).</summary>
    private static string ChangedEvent(object change) =>
        $"event: changed\ndata: {JsonSerializer.Serialize(change, change.GetType(), Json)}\n\n";

    private static string KeepAliveEvent(KeepAliveSignal signal) =>
        $"event: keepAlive\ndata: {JsonSerializer.Serialize(signal, Json)}\n\n";
}
