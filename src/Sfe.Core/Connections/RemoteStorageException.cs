namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// The one error of <see cref="IRemoteStorage"/> and of a sign-in: a provider turns its own into it. The message is
/// for people and for the log: it never carries a token, a code or an address holding one.
/// </summary>
public sealed class RemoteStorageException(
    RemoteErrorKind kind,
    string message,
    TimeSpan? retryAfter = null,
    Exception? inner = null
) : Exception(message, inner)
{
    public RemoteErrorKind Kind { get; } = kind;

    /// <summary>For <see cref="RemoteErrorKind.RateLimited"/>: how long the provider asks to wait.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>For <see cref="RemoteErrorKind.WrongOffset"/>: the bytes of the upload the provider has, when it
    /// tells.</summary>
    public long? Offset { get; init; }
}
