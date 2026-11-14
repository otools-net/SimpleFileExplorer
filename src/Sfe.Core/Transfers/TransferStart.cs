using System.Collections.Concurrent;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// A move or copy between the library and a connection, or between two connections, as
/// <see cref="TransferJobs.Start"/> takes it: the two sides already opened for the job (it disposes them).
/// </summary>
public sealed record TransferStart
{
    /// <summary><see cref="FileJobKind.Move"/> or <see cref="FileJobKind.Copy"/>.</summary>
    public required FileJobKind Kind { get; init; }

    /// <summary>The connection the items are read from; null for the library.</summary>
    public int? FromConnection { get; init; }

    /// <summary>Files and folders to transfer, relative to the source's root.</summary>
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>The folder they go into.</summary>
    public required TransferPlace To { get; init; }

    public ConflictChoice Conflicts { get; init; } = ConflictChoice.Ask;

    public required ITransferEndpoint Source { get; init; }

    public required ITransferEndpoint Target { get; init; }

    public required FileActor Actor { get; init; }

    /// <summary>The person's access in the library, read again as the job runs (as file jobs read it).</summary>
    public required IFileJobAccess Access { get; init; }

    /// <summary>The partial files the library's side writes (given to <see cref="LibraryTransfers.Open"/>).</summary>
    public ConcurrentBag<string> Partials { get; init; } = [];

    /// <summary>
    /// Told the id of a connection whose sign-in ended (<see cref="Connections.RemoteErrorKind.CredentialsEnded"/>):
    /// <see cref="Connections.ConnectionService.MarkNeedsReconnectingAsync"/>.
    /// </summary>
    public Func<int, Task>? SignInEnded { get; init; }

    /// <summary>How the log and the task history name the source connection (never an e-mail); null: its id.</summary>
    public string? FromName { get; init; }

    /// <summary>How the log and the task history name the target connection; null: its id.</summary>
    public string? ToName { get; init; }

    /// <summary>
    /// The failed items of an ended job started again (<see cref="TransferJobs.Retry"/>): a folder already at the
    /// target is gone into, a file there of the same size (and, for a move, content hash) counts as transferred.
    /// </summary>
    public bool Retry { get; internal init; }

    /// <summary>The largest file that may go into a connection (<c>Files:Uploads:MaxFileSizeMegabytes</c>).</summary>
    public int? MaxFileMegabytes { get; internal init; }

    /// <summary>The source as people read it: the library, or the connection.</summary>
    public string SourceText => Text(FromConnection, FromName);

    /// <summary>The target as people read it: the library, or the connection.</summary>
    public string TargetText => Text(To.ConnectionId, ToName);

    private static string Text(int? connection, string? name) =>
        connection is { } id ? name ?? $"connection {id}" : "the library";
}
