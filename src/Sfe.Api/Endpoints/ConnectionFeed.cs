using System.Threading.Channels;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The change feed's part for connections (<see cref="ChangeEndpoints"/>): a folder of a connection changed through
/// SFE (<see cref="ConnectionChanges.FolderChanged"/>, whatever library made the change) is told to a feed whose
/// caller uses that connection in its library and sees that folder there (<see cref="ConnectionAccess"/>, the folder
/// rules), read once per connection while the feed is open. Others never learn the connection or the folder exist.
/// </summary>
internal sealed class ConnectionFeed : IDisposable
{
    private readonly ChangeFeedConnections _services;
    private readonly AccessState _state;
    private readonly Dictionary<int, ConnectionFolders?> _seen = [];
    private readonly Action<int, string> _listener;

    /// <param name="queue">The feed's queue: changes are only queued from the changing request.</param>
    public ConnectionFeed(
        ChangeFeedConnections services,
        AccessState state,
        ChannelWriter<object> queue,
        IHttpContextAccessor accessor
    )
    {
        (_services, _state) = (services, state);
        _listener = (connection, path) =>
        {
            var origin = accessor.HttpContext?.Request.Headers[ChangeEndpoints.TabHeader].ToString();
            queue.TryWrite(new ConnectionFolderChange(path, string.IsNullOrEmpty(origin) ? null : origin, connection));
        };
        if (state.Has(Rights.UseConnections) && state.Has(Rights.Browse))
            services.Changes.FolderChanged += _listener;
    }

    /// <summary>Whether the caller sees the folder changed.</summary>
    public async Task<bool> SeesAsync(ConnectionFolderChange change, CancellationToken cancellationToken)
    {
        if (!_seen.TryGetValue(change.Connection, out var folders))
        {
            folders = await FoldersAsync(change.Connection, cancellationToken);
            _seen[change.Connection] = folders;
        }
        return folders is not null && folders.At(change.Path).IsVisible;
    }

    public void Dispose() => _services.Changes.FolderChanged -= _listener;

    private async Task<ConnectionFolders?> FoldersAsync(int id, CancellationToken cancellationToken)
    {
        var subject = ConnectionEndpoints.SubjectOf(_state);
        var usable = await _services.Access.FindAsync(subject, id, cancellationToken);
        if (usable is null || _services.Providers.Find(usable.Kind) is not { } provider)
            return null;
        var how = (_state.Access.Rights, RemotePaths.ComparisonOf(provider.Capabilities));
        return await _services.Access.FoldersAsync(subject, id, how, cancellationToken);
    }
}
