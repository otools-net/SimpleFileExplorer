using Sfe.Core.Connections;

namespace Sfe.Core.Transfers;

/// <summary>
/// Calls to a provider made by a transfer job (not a person's request): each takes a turn of
/// <see cref="ConnectionGates"/>, waits while the provider asked to wait instead of failing, and is tried again when
/// the provider is down or busy (<see cref="MaxRetries"/> times, backoff 1, 2, 4, 8, 16 s) or slows the account down
/// (its own wait, at most <see cref="MaxRateLimits"/> times). Other failures are thrown at once.
/// </summary>
public sealed class TransferCalls(ConnectionGates gates, TimeProvider clock)
{
    /// <summary>Tries again after the provider was unreachable or no turn came.</summary>
    public const int MaxRetries = 5;

    /// <summary>Tries again after the provider asked to wait (<see cref="RemoteErrorKind.RateLimited"/>).</summary>
    public const int MaxRateLimits = 20;

    /// <summary>A call through the connection; <see cref="RemoteStorageException"/> once it gives up.</summary>
    public Task<T> RunAsync<T>(
        int connectionId,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken
    ) => CallAsync(connectionId, call, cancellationToken);

    /// <inheritdoc cref="RunAsync{T}"/>
    public Task RunAsync(int connectionId, Func<CancellationToken, Task> call, CancellationToken cancellationToken) =>
        CallAsync(connectionId, Answered(call), cancellationToken);

    /// <summary>
    /// A call that changes the account (a commit, a new folder, a delete): one at a time per connection, the write
    /// turn held across its retries.
    /// </summary>
    public async Task<T> WriteAsync<T>(
        int connectionId,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken
    )
    {
        using var write = await gates.EnterWriteAsync(connectionId, cancellationToken);
        return await CallAsync(connectionId, call, cancellationToken);
    }

    /// <inheritdoc cref="WriteAsync{T}"/>
    public Task WriteAsync(int connectionId, Func<CancellationToken, Task> call, CancellationToken cancellationToken) =>
        WriteAsync(connectionId, Answered(call), cancellationToken);

    /// <summary>The wait before try <paramref name="retry"/> (0 for the first retry): 1, 2, 4, 8, 16 s.</summary>
    public static TimeSpan Backoff(int retry) => TimeSpan.FromSeconds(1 << retry);

    private async Task<T> CallAsync<T>(
        int connectionId,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken
    )
    {
        var retries = 0;
        var rateLimits = 0;
        while (true)
        {
            await WaitForProviderAsync(connectionId, cancellationToken);
            var (done, answer, failure) = await TryAsync(connectionId, call, cancellationToken);
            if (done)
                return answer!;

            if (failure!.Kind == RemoteErrorKind.RateLimited)
            {
                if (++rateLimits > MaxRateLimits)
                    throw failure;
                gates.Wait(connectionId, failure.RetryAfter);
                continue;
            }
            if (retries == MaxRetries)
                throw failure;
            await Task.Delay(Backoff(retries++), clock, cancellationToken);
        }
    }

    /// <summary>One try in a turn of the connection: its answer, or the failure worth trying again.</summary>
    private async Task<(bool Done, T? Answer, RemoteStorageException? Failure)> TryAsync<T>(
        int connectionId,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken
    )
    {
        using var turn = await gates.EnterAsync(connectionId, cancellationToken);
        if (turn is null)
            return (false, default, new RemoteStorageException(RemoteErrorKind.Unavailable, "The connection is busy."));
        try
        {
            return (true, await call(cancellationToken), null);
        }
        catch (RemoteStorageException thrown) when (IsPassing(thrown.Kind))
        {
            return (false, default, thrown);
        }
    }

    /// <summary>Waits while the provider asked to (whoever's call it answered).</summary>
    private async Task WaitForProviderAsync(int connectionId, CancellationToken cancellationToken)
    {
        while (gates.WaitLeft(connectionId) is { } left)
            await Task.Delay(left, clock, cancellationToken);
    }

    private static bool IsPassing(RemoteErrorKind kind) =>
        kind is RemoteErrorKind.Unavailable or RemoteErrorKind.RateLimited;

    private static Func<CancellationToken, Task<bool>> Answered(Func<CancellationToken, Task> call) =>
        async cancellationToken =>
        {
            await call(cancellationToken);
            return true;
        };
}
