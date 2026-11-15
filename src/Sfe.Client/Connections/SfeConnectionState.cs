namespace Otools.Net.Sfe.Client;

/// <summary>Whether a connection's sign-in still works.</summary>
public enum SfeConnectionState
{
    /// <summary>A state this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>Usable.</summary>
    Ready,

    /// <summary>The provider refused the sign-in: whoever manages it signs in again (on the web page).</summary>
    NeedsReconnecting,
}
