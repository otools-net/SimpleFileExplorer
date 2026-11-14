using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Transfers;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>A move or copy between the library and a connection, or between two connections.</summary>
/// <param name="Kind"><c>move</c> or <c>copy</c>.</param>
/// <param name="From">Where the items are.</param>
/// <param name="To">The folder they go into.</param>
/// <param name="Conflicts">What to do on a name clash; <c>ask</c> waits for an answer (the job's question).</param>
public sealed record TransferRequest(
    string? Kind,
    TransferSide? From,
    TransferTarget? To,
    ConflictChoice Conflicts = ConflictChoice.Ask
);

/// <param name="Connection">The connection; null for the library.</param>
/// <param name="Paths">The files and folders, relative to the library's root or the connection's.</param>
public sealed record TransferSide(int? Connection, IReadOnlyList<string>? Paths);

/// <param name="Connection">The connection; null for the library.</param>
/// <param name="Path">The folder, relative to the library's root or the connection's (empty: the root).</param>
public sealed record TransferTarget(int? Connection, string? Path);

/// <summary>What the route of transfers starts jobs with (bound with <c>[AsParameters]</c>).</summary>
internal sealed record TransferServices(
    TransferJobs Jobs,
    LibraryTransfers Library,
    TransferCalls Calls,
    RemoteListings Listings,
    AccessChanges Changes,
    ConnectionRuleService Rules
);

/// <summary>
/// What the person's access to the library's side is read with as the job runs (<see cref="FileJobAccess"/>; bound
/// with <c>[AsParameters]</c>).
/// </summary>
internal sealed record TransferAccessServices(
    AccessService Accounts,
    RightsEvaluator Rights,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder Work
);
