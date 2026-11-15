namespace Otools.Net.Sfe.Client;

/// <summary>How the server is open to others.</summary>
public enum SfeServerMode
{
    /// <summary>A mode this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>Answers its own computer only, to administrators.</summary>
    Local,

    /// <summary>Answers other computers, with accounts and visitors.</summary>
    External,
}
