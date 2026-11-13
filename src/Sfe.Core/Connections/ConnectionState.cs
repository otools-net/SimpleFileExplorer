namespace Sfe.Core.Connections;

public enum ConnectionState
{
    /// <summary>Signed in: the storage can be opened.</summary>
    Ready,

    /// <summary>
    /// The provider no longer accepts the sign-in (revoked there, expired, keys lost with a restored backup): someone
    /// who manages the connection signs in again; its groups and rules stay.
    /// </summary>
    NeedsReconnecting,
}
