using System.Collections.Concurrent;
using Sfe.Core.Files;

namespace Sfe.Core.Transfers;

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

    /// <summary>The partial files the library's side writes (the bag given to <see cref="LibraryTransfers.Open"/>).</summary>
    public ConcurrentBag<string> Partials { get; init; } = [];

    /// <summary>
    /// Told the id of a connection whose sign-in ended (<see cref="Connections.RemoteErrorKind.CredentialsEnded"/>):
    /// <see cref="Connections.ConnectionService.MarkNeedsReconnectingAsync"/>.
    /// </summary>
    public Func<int, Task>? SignInEnded { get; init; }
}
