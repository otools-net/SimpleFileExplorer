namespace Sfe.Core.Access;

/// <summary>
/// A folder move in progress for the rules (<see cref="FolderRuleService.BeginMoveAsync"/>): holds the library's
/// lock until it is completed, aborted or disposed.
/// </summary>
public sealed class FolderRuleMove : IAsyncDisposable
{
    private readonly FolderRuleMoves _moves;
    private readonly SemaphoreSlim _gate;
    private readonly int _tenantId;
    private readonly IReadOnlyList<int> _originals;
    private readonly IReadOnlyList<int> _copies;
    private readonly IReadOnlyDictionary<int, string>? _previousPaths;
    private int _released;

    internal FolderRuleMove(
        FolderRuleMoves moves,
        SemaphoreSlim gate,
        int tenantId,
        IReadOnlyList<int> originals,
        IReadOnlyList<int> copies,
        IReadOnlyDictionary<int, string>? previousPaths
    )
    {
        _moves = moves;
        _gate = gate;
        _tenantId = tenantId;
        _originals = originals;
        _copies = copies;
        _previousPaths = previousPaths;
    }

    /// <summary>The folder moved on the disk: the rules at the old paths go.</summary>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        try
        {
            await _moves.DeleteAsync(_tenantId, _originals, cancellationToken);
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
            if (_previousPaths is not null)
                await _moves.RestorePathsAsync(_tenantId, _previousPaths, cancellationToken);
            else
                await _moves.DeleteAsync(_tenantId, _copies, cancellationToken);
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
