namespace Otools.Net.Sfe.Client;

/// <summary>
/// 401 or 403: the token is wrong, expired or revoked (401), or it or its account lacks the right (403;
/// <c>session-only</c> for a route a token may not call).
/// </summary>
public sealed class SfeRefusedException : SfeApiException
{
    /// <summary>A 401 or 403 with the problem read from the answer.</summary>
    /// <param name="statusCode">The HTTP status of the answer.</param>
    /// <param name="problem">The problem of its body.</param>
    public SfeRefusedException(int statusCode, SfeProblem problem)
        : base(statusCode, problem) { }
}
