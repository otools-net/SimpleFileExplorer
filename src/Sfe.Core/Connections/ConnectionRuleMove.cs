namespace Sfe.Core.Connections;

/// <summary>
/// A folder move of a connection in progress for the rules (<see cref="ConnectionRuleService.BeginMoveAsync"/>): holds
/// the connection's lock until it is completed, aborted or disposed, as the library's moves do.
/// </summary>
public sealed class ConnectionRuleMove : IAsyncDisposable
{
    private readonly ConnectionRuleMoves _moves;
    private readonly SemaphoreSlim _gate;
    private readonly ConnectionRuleMoveRows _rows;
    private int _released;

    internal ConnectionRuleMove(ConnectionRuleMoves moves, SemaphoreSlim gate, ConnectionRuleMoveRows rows)
    {
        _moves = moves;
        _gate = gate;
        _rows = rows;
    }

    /// <summary>The libraries other than the request's whose rules follow the folder (or go stale).</summary>
    public IReadOnlyList<int> OtherLibraries => _rows.OtherLibraries;

    /// <summary>
    /// The provider moved the folder: the rules at the old paths go. <paramref name="moved"/> is the item where the
    /// provider put it: its path when it chose another name, its id recorded on the folder's own rules.
    /// </summary>
    public async Task CompleteAsync(RemoteItem? moved, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        try
        {
            await _moves.CompleteAsync(_rows, moved, cancellationToken);
        }
        finally
        {
            Release();
        }
    }

    /// <summary>The folder didn't move: the rules are as they were before the move began.</summary>
    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        try
        {
            await _moves.AbortAsync(_rows, cancellationToken);
        }
        finally
        {
            Release();
        }
    }

    /// <summary>
    /// Releases the lock; without <see cref="CompleteAsync"/> or <see cref="AbortAsync"/> both sets of rules stay.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _released) != 0, this);

    private void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _gate.Release();
    }
}
