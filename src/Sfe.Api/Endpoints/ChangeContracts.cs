using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.KeepAlive;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Path">The folder changed (root-relative).</param>
/// <param name="Origin">The tab whose request changed it (`X-SFE-Tab`), which needs no notice; null from a background
/// job.</param>
/// <param name="Outside">Changed outside SFE (another program, the share's other users), seen in a folder a tab
/// watches (`PUT files/changes/watch`).</param>
public sealed record FolderChange(string Path, string? Origin, bool Outside = false);

/// <param name="Path">The folder the tab shows (root-relative, "" for the top of the library or of the connection);
/// null: none.</param>
/// <param name="Connection">The connection whose folder it is; null for the library's.</param>
public sealed record FolderWatchRequest(string? Path, int? Connection = null);

/// <param name="Watching">The folder is followed for changes made outside SFE: false when the tab has no feed open
/// in this library, or the folder is missing, hidden from the caller, or past the server's limit; for a connection's,
/// when the caller can't use it or its storage service tells no changes.</param>
public sealed record FolderWatchAnswer(bool Watching);

/// <param name="Path">The folder (root-relative) whose items being written moved on, started or ended.</param>
/// <param name="Items">The items being written there now (those the caller may see); empty when the last one ended.
/// </param>
public sealed record FolderWriting(string Path, IReadOnlyList<ItemWriting> Items);

/// <summary>A folder whose items being written moved on (<see cref="ItemsBeingWritten.FolderProgressed"/>).</summary>
internal sealed record WritingSignal(string Path);

/// <param name="State">How keep alive goes in the space: `none` (off, or nothing pinged), `ok`, `failing`.</param>
public sealed record KeepAliveSignal(KeepAliveState State);

/// <param name="Id">The run, in the task history (`GET tasks/runs/{id}`); negative when it couldn't be written there.
/// </param>
/// <param name="Task">The task that ran.</param>
/// <param name="Path">A scan's folder (root-relative), a connection sync's connection; null for the others.</param>
/// <param name="Outcome">How it ended: `completed`, `cancelled` or `failed`.</param>
/// <param name="Summary">What it did, or why it failed.</param>
/// <param name="Finished">When it ended (the server's clock): pass the latest as `since` when opening the feed again.
/// </param>
/// <param name="Mine">The caller started it; false: a failure told to those who see the tasks.</param>
public sealed record TaskEnded(
    long Id,
    BackgroundTaskKind Task,
    string? Path,
    TaskOutcome Outcome,
    string? Summary,
    DateTimeOffset? Finished,
    bool Mine
);

/// <summary>Something came into or left the trash.</summary>
public sealed record TrashSignal;

/// <param name="Path">The folder changed, from the connection's root.</param>
/// <param name="Origin">The tab whose request changed it (`X-SFE-Tab`); null from a background job.</param>
/// <param name="Connection">The connection's id.</param>
/// <param name="Outside">Changed outside SFE (at the storage service), seen in a folder a tab watches.</param>
public sealed record ConnectionFolderChange(string Path, string? Origin, int Connection, bool Outside = false);

/// <summary>What the change feed reads connections with (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ChangeFeedConnections(
    Otools.Net.Sfe.Core.Connections.ConnectionChanges Changes,
    Otools.Net.Sfe.Core.Connections.ConnectionAccess Access,
    Otools.Net.Sfe.Core.Connections.ConnectionProviders Providers
);
