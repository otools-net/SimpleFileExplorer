namespace Otools.Net.Sfe.Client;

/// <summary>
/// Why a connection's call failed (<see cref="SfeApiException.ConnectionProblem"/>, from the problem's
/// <c>connection-…</c> word): what to do differs, whatever the status.
/// </summary>
public enum SfeConnectionProblem
{
    /// <summary>A <c>connection-…</c> word this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>
    /// 409 <c>connection-needs-reconnecting</c>: the provider no longer takes the sign-in; whoever manages the
    /// connection signs in again (on the web page).
    /// </summary>
    NeedsReconnecting,

    /// <summary>
    /// 429 <c>connection-rate-limited</c>: the provider asks to wait
    /// (<see cref="SfeRateLimitedException.RetryAfter"/>); retried as any 429.
    /// </summary>
    RateLimited,

    /// <summary>502 <c>connection-unavailable</c>: the provider can't be reached or failed; later may work.</summary>
    Unavailable,

    /// <summary>403 <c>connection-refused</c>: the provider doesn't let this account do it.</summary>
    Refused,

    /// <summary>409 <c>connection-not-supported</c>: the provider lacks the function.</summary>
    NotSupported,

    /// <summary>409 <c>connection-unusable</c>: the connection is off, or its kind or level not allowed.</summary>
    Unusable,

    /// <summary>
    /// 409 <c>connection-not-indexed</c>: the library's index doesn't hold the connection's files yet (tags are kept
    /// there); its sync has started: try again once it ends (<see cref="SfeConnection.GetIndexAsync"/>).
    /// </summary>
    NotIndexed,
}
