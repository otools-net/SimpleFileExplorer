namespace Sfe.Core.Connections;

public enum RemoteErrorKind
{
    /// <summary>Nothing at the path (or its folder is missing).</summary>
    NotFound,

    /// <summary>Something of that name is already there.</summary>
    Exists,

    /// <summary>
    /// The provider does not let this account do it (a read-only shared folder, a missing permission).
    /// </summary>
    Refused,

    /// <summary>
    /// The sign-in no longer holds (revoked, expired, a wrong code): the connection needs reconnecting.
    /// </summary>
    CredentialsEnded,

    /// <summary>Too many requests: wait <see cref="RemoteStorageException.RetryAfter"/>.</summary>
    RateLimited,

    /// <summary>The file is too large for the provider, or the storage is full.</summary>
    TooLarge,

    /// <summary>The provider cannot be reached or fails on its side; trying later may work.</summary>
    Unavailable,

    /// <summary>The provider refuses the name (characters, length, a reserved name).</summary>
    InvalidName,

    /// <summary>The provider lacks the function (<see cref="ProviderCapabilities"/>).</summary>
    NotSupported,
}
