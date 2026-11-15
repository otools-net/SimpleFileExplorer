using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Files of a connection tagged together (<see cref="RemoteScopeTagger"/>): those of a folder (and its sub-folders),
/// or a search's matches, among the rows the library's index holds, where folder rules let the asker see them.
/// </summary>
public sealed record RemoteTagScope
{
    /// <summary>The connection as it is now: an index of another root or account holds none of its files.</summary>
    public required ConnectionIndexTarget Target { get; init; }

    /// <summary>The folder or search, connection path normalised (as <see cref="TagScope"/> for the library).</summary>
    public required TagScope Scope { get; init; }

    /// <summary>Where folder rules let the asker see files (connection paths); null: everywhere.</summary>
    public PathFilter? Visible { get; init; }

    /// <summary>Provider ids of items a rule set at another path hides (see <see cref="RemoteSearchAsk"/>).</summary>
    public IReadOnlySet<string> HiddenIds { get; init; } = new HashSet<string>();

    /// <summary>Where the asker may tag (<c>tags.assign</c>): a change leaves the other files alone; null: wherever
    /// it sees.</summary>
    public PathFilter? Taggable { get; init; }

    internal string Folder => Scope.Path ?? string.Empty;
}
