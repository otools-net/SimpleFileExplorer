using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// What SFE changed in connections' files (routes, uploads, transfer jobs), told at once to whoever keeps something
/// of them: each library's index of the connection (<see cref="Indexing.ConnectionIndexUpdates"/>), awaited, and the
/// change feeds of open listings (<see cref="FolderChanged"/>). One for the server: a connection is the same in every
/// library that uses it. A failing listener is logged, never fails the change (the next sync puts the index right).
/// </summary>
public sealed class ConnectionChanges(ILogger<ConnectionChanges> logger)
{
    private ImmutableList<Func<ConnectionItemChange, CancellationToken, Task>> _listeners = [];

    /// <summary>A folder of a connection changed: (connection id, folder path from its root).</summary>
    public event Action<int, string>? FolderChanged;

    /// <summary>Hears every change from now on, until the answer is disposed.</summary>
    public IDisposable Listen(Func<ConnectionItemChange, CancellationToken, Task> listener)
    {
        ImmutableInterlocked.Update(ref _listeners, list => list.Add(listener));
        return new Listening(this, listener);
    }

    public async Task ReportAsync(ConnectionItemChange change, CancellationToken cancellationToken)
    {
        foreach (var listener in _listeners)
            await TellAsync(listener, change, cancellationToken);
        foreach (var folder in change.Folders())
            FolderChanged?.Invoke(change.ConnectionId, folder);
    }

    private async Task TellAsync(
        Func<ConnectionItemChange, CancellationToken, Task> listener,
        ConnectionItemChange change,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await listener(change, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(
                error,
                "A change in connection {ConnectionId} could not be written into an index; its next sync will",
                change.ConnectionId
            );
        }
    }

    private sealed class Listening(
        ConnectionChanges changes,
        Func<ConnectionItemChange, CancellationToken, Task> listener
    ) : IDisposable
    {
        public void Dispose() => ImmutableInterlocked.Update(ref changes._listeners, list => list.Remove(listener));
    }
}
