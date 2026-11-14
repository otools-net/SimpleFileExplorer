using System.Collections.Concurrent;
using Sfe.Core.Connections;
using Sfe.Core.Files;
using Sfe.Core.Transfers;

namespace Sfe.Api.Endpoints;

/// <summary>
/// The two sides of a transfer the route starts: a connection checked for the session first (404, 403, 409 at once,
/// as the connection's own routes answer), then opened again for the job, which keeps it for its lifetime (what the
/// request opened is disposed with its answer). The library's side is opened with the person's access.
/// </summary>
internal sealed class TransferOpening
{
    private readonly ConnectionFileServices _files;
    private readonly TransferServices _transfers;
    private readonly string _right;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<int, ProviderCapabilities> _capabilities = [];

    public TransferOpening(
        ConnectionFileServices files,
        TransferServices transfers,
        string right,
        CancellationToken cancellationToken
    )
    {
        (_files, _transfers, _right, _cancellationToken) = (files, transfers, right, cancellationToken);
        var connections = files.Connections;
        SignInEnded = id => connections.MarkNeedsReconnectingAsync(id);
    }

    /// <summary>Marks a connection whose sign-in ended; it holds nothing of the request.</summary>
    public Func<int, Task> SignInEnded { get; }

    /// <summary>The answer when the session may not use the connection this way; null for the library.</summary>
    /// <param name="write">Written to: a connection the session only reads answers 403.</param>
    public async Task<IResult?> CheckAsync(int? connection, bool write)
    {
        if (connection is not { } id)
            return null;
        var (open, answer) = await _files.OpenAsync(id, _right, write, _cancellationToken);
        if (open is null)
            return answer;
        _capabilities[id] = open.Capabilities;
        return null;
    }

    /// <summary>The side the job keeps (it disposes it), or the answer when the connection can't be opened.</summary>
    public async Task<(ITransferEndpoint? Side, IResult? Answer)> OpenAsync(
        int? connection,
        FileActor actor,
        IFileJobAccess access,
        ConcurrentBag<string> partials
    )
    {
        if (connection is not { } id)
            return (_transfers.Library.Open(actor, access, _right, partials), null);
        var opened = await _files.Connections.OpenAsync(id, _cancellationToken);
        if (opened.Value is not { } storage)
            return (null, ConnectionProblems.OfOpening(opened.Problem));
        var side = new RemoteTransferEndpoint(id, storage, _capabilities[id], _transfers.Calls, _transfers.Listings);
        return (side, null);
    }
}
