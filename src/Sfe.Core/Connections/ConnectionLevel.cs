namespace Sfe.Core.Connections;

/// <summary>Whose a connection is, and so who can ever use it.</summary>
public enum ConnectionLevel
{
    /// <summary>
    /// Private to the account that made it, in every library it works in. Administrators see that it exists and
    /// can remove it, never its content.
    /// </summary>
    Account,

    /// <summary>One library's: its members by their groups.</summary>
    Library,

    /// <summary>The server's: offered to libraries, whose administrators say which of their groups use it.</summary>
    Server,
}
