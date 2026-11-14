using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// What a group of a library may do in a library- or server-level <see cref="Connection"/>:
/// <see cref="FolderLevel.Read"/> or <see cref="FolderLevel.Write"/>. A group without a row does not see the
/// connection. For a server-level connection each library's administrators set the rows of their own groups.
/// </summary>
public sealed class ConnectionGroup
{
    public int ConnectionId { get; set; }
    public Connection? Connection { get; set; }
    public int GroupId { get; set; }
    public UserGroup? Group { get; set; }

    /// <summary>Read or Write; never <see cref="FolderLevel.Hidden"/> (no row says that).</summary>
    public FolderLevel Level { get; set; }
}
