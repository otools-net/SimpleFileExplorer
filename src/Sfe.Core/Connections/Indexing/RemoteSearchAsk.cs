using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// A page of a search of a connection's files in the library's index (<see cref="RemoteIndexSearch"/>).
/// </summary>
public sealed record RemoteSearchAsk
{
    /// <summary>The connection as it is now: an index made of another root folder or account is not read.</summary>
    public required ConnectionIndexTarget Target { get; init; }

    /// <summary>The search, in the library's query language.</summary>
    public string? Query { get; init; }

    /// <summary>The folder searched, with everything below it (normalised, relative to the root; empty: all).</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>The <c>next</c> of the previous page, for the same sort; null for the first page.</summary>
    public string? After { get; init; }

    public int Limit { get; init; } = 100;

    public SortField Sort { get; init; } = SortField.Path;

    public bool Descending { get; init; }

    /// <summary>Where folder rules let the asker see files (connection paths); null: everywhere.</summary>
    public PathFilter? Visible { get; init; }

    /// <summary>
    /// The provider's ids of the items a rule set on them at another path hides (renamed or moved outside SFE): such
    /// files, and everything below such folders, are left out.
    /// </summary>
    public IReadOnlySet<string> HiddenIds { get; init; } = new HashSet<string>();
}
