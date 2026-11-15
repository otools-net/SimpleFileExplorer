using System.Collections.Generic;
using Otools.Net.Sfe.Client.Files;

namespace Otools.Net.Sfe.Client.Connections;

// Requests and answers of the connection routes (api/v1/connections…), the parts the facade uses.

/// <summary>The answer of <c>GET connections</c>: the facade reads the usable ones only.</summary>
internal sealed class ConnectionsAnswer
{
    public List<UsableConnection> Usable { get; init; } = [];
}

/// <summary>One of <see cref="ConnectionsAnswer.Usable"/>.</summary>
internal sealed class UsableConnection
{
    public int Id { get; init; }
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public SfeConnectionLevel Level { get; init; }
    public SfeConnectionState State { get; init; }
    public SfeConnectionAccess Access { get; init; }
}

/// <summary>A page of <c>GET connections/{id}/browse</c>: the library's listing and the connection's parts.</summary>
internal sealed class ConnectionPage
{
    public ListingPage Listing { get; init; } = new();
    public bool HasMore { get; init; }
    public SfeConnectionCapabilities Capabilities { get; init; } = new();
}

/// <summary>The body of <c>POST connections/{id}/files/transfer</c>.</summary>
internal sealed record InPlaceTransfer(
    SfeJobKind Kind,
    IReadOnlyList<string> Paths,
    string Target,
    string Conflict,
    bool? KeepAccess
);

/// <summary>The answer of <c>POST connections/{id}/files/transfer</c>.</summary>
internal sealed class InPlaceTransferAnswer
{
    public List<SfeConnectionTransferResult> Items { get; init; } = [];
}

/// <summary>The body of <c>POST files/jobs/transfer</c>: between the library and a connection, or two.</summary>
internal sealed record TransferRequest(
    SfeJobKind Kind,
    TransferSide From,
    TransferTarget To,
    SfeConflictChoice Conflicts
);

/// <summary>Where a transfer's items are (<see cref="Connection"/> null: the library).</summary>
internal sealed record TransferSide(int? Connection, IReadOnlyList<string> Paths);

/// <summary>The folder a transfer's items go into (<see cref="Connection"/> null: the library).</summary>
internal sealed record TransferTarget(int? Connection, string Path);
