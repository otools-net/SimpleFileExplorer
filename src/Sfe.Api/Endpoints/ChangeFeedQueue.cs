using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.KeepAlive;
using Otools.Net.Sfe.Core.Library.Watching;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What one change feed listens to while it is open: folders changed in the library and in connections, keep alive's
/// state, the trash (for callers who may open it), the progress of items being written, changes made outside SFE (in
/// the folder its tab shows, <see cref="Watch"/>, and those others watch). The events are only queued by
/// the request (or job, or keep alive's loop) that raised them; the feed's own loop takes them as the text of
/// server-sent events, keeping those the caller may see. Disposing it stops listening.
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
    private readonly Lock _watchGate = new();
    private FolderWatch? _watch;
    private bool _closed;

    // Holds a FolderChange, a ConnectionFolderChange, a KeepAliveSignal, a TrashSignal or a WritingSignal.
    private readonly Channel<object> _changes = Channel.CreateBounded<object>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true }
    );

    public ChangeFeedQueue(ChangeFeedServices services, ChangeFeedConnections connections, AccessState state)
    {
        (_services, _state) = (services, state);
        services.Browser.FolderChanged += OnChanged;
        services.Watchers.Changed += OnOutside;
        _connections = new ConnectionFeed(connections, state, _changes.Writer, services.Accessor);
        services.KeepAlive.StateChanged += OnKeepAlive;
        services.Writing.FolderProgressed += OnWriting;
        // The trash's count is for those who may open it.
        if (state.Access.Rights.Contains(Rights.DeleteFiles) || state.Access.Rights.Contains(Rights.DeletePermanently))
            services.Trash.EntriesChanged += OnTrash;
    }

    /// <summary>The library the feed tells of.</summary>
    public int TenantId => _state.TenantId;

    /// <summary>
    /// Follows <paramref name="folder"/> (root-relative; null: none) for changes made outside SFE while the feed is
    /// open, instead of the folder followed before (taken first, so the same folder isn't let go of in between).
    /// False when it isn't followed: hidden from the caller, missing, too many followed, the feed closed.
    /// </summary>
    public bool Watch(string? folder)
    {
        var next =
            folder is not null && _state.Check(_services.Paths, folder, Rights.Browse) == PathVerdict.Allowed
                ? _services.Watchers.Watch(folder)
                : null;
        FolderWatch? previous;
        lock (_watchGate)
        {
            if (_closed)
            {
                next?.Dispose();
                return false;
            }
            (previous, _watch) = (_watch, next);
        }
        previous?.Dispose();
        return next?.IsActive == true;
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
        _services.Watchers.Changed -= OnOutside;
        lock (_watchGate)
        {
            _closed = true;
            _watch?.Dispose();
            _watch = null;
        }
        _services.KeepAlive.StateChanged -= OnKeepAlive;
        _services.Writing.FolderProgressed -= OnWriting;
        _services.Trash.EntriesChanged -= OnTrash;
        _connections.Dispose();
    }

    private async Task<string> TakeQueuedAsync(CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var trashChanged = false;
        var writing = new HashSet<string>(LibraryPaths.PathComparer);
        while (_changes.Reader.TryRead(out var item))
        {
            if (item is TrashSignal)
                trashChanged = true;
            else if (item is WritingSignal signal)
                writing.Add(signal.Path);
            else if (await EventOfAsync(item, cancellationToken) is { } seen)
                text.Append(seen);
        }
        if (trashChanged)
            text.Append(ChangeEndpoints.TrashEvent);
        foreach (var folder in writing)
            text.Append(WritingEvent(folder));
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

    private void OnOutside(string path) => _changes.Writer.TryWrite(new FolderChange(path, null, Outside: true));

    private void OnWriting(string folder) => _changes.Writer.TryWrite(new WritingSignal(folder));

    /// <summary>
    /// `writing`: the items being written in <paramref name="folder"/> now (read when sent: the latest), those the
    /// caller sees; nothing when it may not see the folder.
    /// </summary>
    private string WritingEvent(string folder)
    {
        if (_state.Check(_services.Paths, folder, Rights.Browse) != PathVerdict.Allowed)
            return string.Empty;
        var items = _services
            .Writing.In(folder)
            .Where(i => _state.Check(_services.Paths, Join(folder, i.Name), Rights.Browse) == PathVerdict.Allowed)
            .ToList();
        return $"event: writing\ndata: {JsonSerializer.Serialize(new FolderWriting(folder, items), Json)}\n\n";
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : $"{folder}/{name}";

    private void OnKeepAlive(KeepAliveState keepAliveState) =>
        _changes.Writer.TryWrite(new KeepAliveSignal(keepAliveState));

    private void OnTrash() => _changes.Writer.TryWrite(new TrashSignal());

    /// <summary>`changed`, with a library folder (`path`, `origin`) or a connection's (and its `connection`).</summary>
    private static string ChangedEvent(object change) =>
        $"event: changed\ndata: {JsonSerializer.Serialize(change, change.GetType(), Json)}\n\n";

    private static string KeepAliveEvent(KeepAliveSignal signal) =>
        $"event: keepAlive\ndata: {JsonSerializer.Serialize(signal, Json)}\n\n";
}
