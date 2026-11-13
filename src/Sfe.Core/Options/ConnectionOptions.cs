namespace Sfe.Core.Options;

/// <summary>
/// Connections to storage outside the library (<c>Sfe:Connections</c>): whoever runs the server decides whether
/// they exist at all, of which kinds and at which levels. The server calls the providers' addresses itself, so
/// these are locked from the environment (<c>Sfe:Locked</c>: <c>connections.*</c>) on a server that must not.
/// </summary>
public sealed class ConnectionOptions
{
    public const string AccountLevel = "account";
    public const string LibraryLevel = "library";
    public const string ServerLevel = "server";

    /// <summary>Off: no connection is listed, opened or made, whatever the rights say.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The kinds allowed (<c>dropbox</c>); empty = every kind the server knows.</summary>
    public List<string> Providers { get; set; } = [];

    /// <summary>
    /// The levels a connection may be made at: <c>account</c> (private to its owner), <c>library</c> (a library's
    /// members), <c>server</c> (offered to libraries). Connections of a level taken out stay but are not usable.
    /// </summary>
    public List<string> Levels { get; set; } = [AccountLevel, LibraryLevel, ServerLevel];

    /// <summary>
    /// A provider whose address is typed by a person may be on this computer or its network (loopback, private and
    /// link-local addresses). Only in the configuration: on a server open to others it lets them reach what the
    /// network hides. A Local server allows it without this.
    /// </summary>
    public bool AllowPrivateAddresses { get; set; }

    public DropboxOptions Dropbox { get; set; } = new();
}
