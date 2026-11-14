using System.Collections.Concurrent;

namespace Sfe.Core.Transfers;

/// <summary>
/// The failed items of the ended transfer jobs (<see cref="TransferRetry"/>), in memory for
/// <see cref="Kept"/>: a restart of the server forgets them. Each is started again once.
/// </summary>
internal sealed class TransferRetries
{
    public static readonly TimeSpan Kept = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<int, TransferRetry> _ended = new();

    public TransferRetry? Find(int jobId)
    {
        Prune();
        return _ended.GetValueOrDefault(jobId);
    }

    /// <summary>Kept when it has items to start again.</summary>
    public void Keep(TransferRetry retry)
    {
        Prune();
        if (retry.Paths.Count > 0)
            _ended[retry.JobId] = retry;
    }

    /// <summary>Taken to be started again; false when it is gone (started again already, or too old).</summary>
    public bool Claim(int jobId) => _ended.TryRemove(jobId, out _);

    private void Prune()
    {
        var limit = DateTimeOffset.UtcNow - Kept;
        foreach (var (id, retry) in _ended)
        {
            if (retry.Ended < limit)
                _ended.TryRemove(id, out _);
        }
    }
}
