using System;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// 429: too many calls of one kind; thrown once the retries of <see cref="SfeClientOptions.MaxRetries"/> are spent,
/// or at once when the wait asked is longer than <see cref="SfeClientOptions.MaxRetryDelay"/>.
/// </summary>
public sealed class SfeRateLimitedException : SfeApiException
{
    /// <summary>A 429 with the problem read from the answer and its <c>Retry-After</c>.</summary>
    /// <param name="statusCode">The HTTP status of the answer.</param>
    /// <param name="problem">The problem of its body.</param>
    /// <param name="retryAfter">How long the server asked to wait; null when it didn't say.</param>
    public SfeRateLimitedException(int statusCode, SfeProblem problem, TimeSpan? retryAfter)
        : base(statusCode, problem)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>How long the server asked to wait before calling again; null when it didn't say.</summary>
    public TimeSpan? RetryAfter { get; }
}
