using System;
using System.Net.Http;

namespace Otools.Net.Sfe.Client.Http;

/// <summary>How long to wait before trying a refused call again, and whether to try at all.</summary>
internal static class RetryDelays
{
    /// <summary>The wait asked by the answer's <c>Retry-After</c> (seconds or a date); null when it has none.</summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta;
        if (header?.Date is { } date)
            return date - DateTimeOffset.UtcNow is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        return null;
    }

    /// <summary>
    /// The wait before try <paramref name="attempt"/> + 1 of a call refused with <paramref name="statusCode"/>; null
    /// when it isn't tried again (another status, no retries left, a server not ready, or a wait too long).
    /// </summary>
    public static TimeSpan? Next(
        int statusCode,
        SfeProblem problem,
        TimeSpan? retryAfter,
        int attempt,
        SfeClientOptions options
    )
    {
        if (statusCode is not (429 or 503) || attempt >= options.MaxRetries)
            return null;
        if (SfeServerNotReadyException.IsNotReady(problem.Word))
            return null;
        if (retryAfter is { } asked)
            return asked <= options.MaxRetryDelay ? asked : null;
        var backoff = Backoff(attempt, options.RetryDelay);
        return backoff <= options.MaxRetryDelay ? backoff : options.MaxRetryDelay;
    }

    /// <summary>The wait before a new try of something cut off (a transfer): doubled at each try.</summary>
    public static TimeSpan Backoff(int attempt, TimeSpan first) =>
        TimeSpan.FromTicks(first.Ticks * (1L << Math.Min(attempt, 10)));
}
