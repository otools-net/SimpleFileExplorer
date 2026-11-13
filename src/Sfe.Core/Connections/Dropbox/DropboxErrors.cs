using Dropbox.Api;

namespace Sfe.Core.Connections.Dropbox;

/// <summary>
/// What the Dropbox SDK throws, as one <see cref="RemoteStorageException"/> (the kinds seen in the spike,
/// agents/docs/development/connections-plan.md, phase 0). The messages name no token, code or address.
/// </summary>
internal static class DropboxErrors
{
    /// <summary>The exception to throw for <paramref name="error"/>; null when it is not one of Dropbox's.</summary>
    public static RemoteStorageException? Map(Exception error) =>
        error switch
        {
            RemoteStorageException => null,
            RateLimitException limit => new(
                RemoteErrorKind.RateLimited,
                "Dropbox asks to wait before the next request.",
                TimeSpan.FromSeconds(Math.Max(1, limit.RetryAfter)),
                error
            ),
            AuthException or OAuth2Exception => new(
                RemoteErrorKind.CredentialsEnded,
                "Dropbox refused the sign-in.",
                inner: error
            ),
            BadInputException => new(RemoteErrorKind.Refused, "Dropbox refused the request.", inner: error),
            AccessException => new(RemoteErrorKind.Refused, "Dropbox refused access.", inner: error),
            RetryException or HttpException or HttpRequestException or TaskCanceledException => new(
                RemoteErrorKind.Unavailable,
                "Dropbox cannot be reached from this server now.",
                inner: error
            ),
            // The SDK's decoder, given an answer of another shape (a proxy's page, an empty 404).
            InvalidOperationException when error.Source == "Dropbox.Api" => new(
                RemoteErrorKind.Unavailable,
                "Dropbox answered something unexpected.",
                inner: error
            ),
            _ => null,
        };

    /// <summary>Runs a call to Dropbox, its failures turned into <see cref="RemoteStorageException"/>.</summary>
    public static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception error) when (Map(error) is { } mapped)
        {
            throw mapped;
        }
    }
}
