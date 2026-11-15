namespace Otools.Net.Sfe.Client;

/// <summary>Whose a connection is.</summary>
public enum SfeConnectionLevel
{
    /// <summary>A level this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>One account's own, private.</summary>
    Account,

    /// <summary>A library's, used by its groups.</summary>
    Library,

    /// <summary>The server's, offered to some libraries or all.</summary>
    Server,
}
