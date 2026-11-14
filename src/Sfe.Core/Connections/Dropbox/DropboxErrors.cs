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
            DropboxException when IsApiError(error) => FromSummary(error),
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

    /// <summary>
    /// An endpoint's own error (<c>ApiException&lt;T&gt;</c>, one type per endpoint): its message is Dropbox's
    /// <c>error_summary</c> (<c>path/not_found/..</c>), read by the words in it.
    /// </summary>
    private static RemoteStorageException FromSummary(Exception error)
    {
        var summary = error.Message;
        var kind = summary switch
        {
            // An upload's finish at another length than Dropbox has (lookup_failed/incorrect_offset).
            _ when summary.Contains("incorrect_offset", StringComparison.Ordinal) => RemoteErrorKind.WrongOffset,
            _ when summary.Contains("not_found", StringComparison.Ordinal)
                    || summary.Contains("not_folder", StringComparison.Ordinal)
                    || summary.Contains("not_file", StringComparison.Ordinal) => RemoteErrorKind.NotFound,
            _ when summary.Contains("conflict", StringComparison.Ordinal) => RemoteErrorKind.Exists,
            _ when summary.Contains("too_many_write_operations", StringComparison.Ordinal) =>
                RemoteErrorKind.RateLimited,
            _ when summary.Contains("too_large", StringComparison.Ordinal)
                    || summary.Contains("insufficient_space", StringComparison.Ordinal)
                    || summary.Contains("insufficient_quota", StringComparison.Ordinal) => RemoteErrorKind.TooLarge,
            _ when summary.Contains("malformed_path", StringComparison.Ordinal)
                    || summary.Contains("disallowed_name", StringComparison.Ordinal)
                    || summary.Contains("cant_move_folder_into_itself", StringComparison.Ordinal) =>
                RemoteErrorKind.InvalidName,
            // A move or copy of more files than Dropbox moves in one call.
            _ when summary.Contains("too_many_files", StringComparison.Ordinal) => RemoteErrorKind.Refused,
            _ => RemoteErrorKind.Refused,
        };
        // The summary names no token; it may name the path, which the caller already knows.
        return new RemoteStorageException(kind, $"Dropbox: {summary.TrimEnd('.', '/')}", inner: error);
    }

    private static bool IsApiError(Exception error) =>
        error.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(ApiException<>);

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
