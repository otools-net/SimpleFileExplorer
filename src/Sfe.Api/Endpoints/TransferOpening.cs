using System.Collections.Concurrent;
using Sfe.Api.Security;
using Sfe.Core.Connections;
using Sfe.Core.Files;
using Sfe.Core.Transfers;

namespace Sfe.Api.Endpoints;

/// <summary>
/// The two sides of a transfer the route starts: a connection checked for the session first (404, 403, 409 at once,
/// as the connection's own routes answer; the target folder by its folder rules), then opened again for the job,
/// which keeps it for its lifetime (what the request opened is disposed with its answer) and checks the person's
/// access to it again at every item (<see cref="ConnectionJobAccess"/>). The library's side is opened with the
/// person's access.
/// </summary>
internal sealed class TransferOpening
{
    private readonly ConnectionFileServices _files;
    private readonly TransferServices _transfers;
    private readonly TransferAccessServices _people;
    private readonly string _right;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<int, ProviderCapabilities> _capabilities = [];
    private readonly Dictionary<int, string> _names = [];

    public TransferOpening(
        ConnectionFileServices files,
        TransferServices transfers,
        TransferAccessServices people,
        string right,
        CancellationToken cancellationToken
    )
    {
        (_files, _transfers, _people) = (files, transfers, people);
        (_right, _cancellationToken) = (right, cancellationToken);
        var connections = files.Connections;
        SignInEnded = id => connections.MarkNeedsReconnectingAsync(id);
    }

    /// <summary>Marks a connection whose sign-in ended; it holds nothing of the request.</summary>
    public Func<int, Task> SignInEnded { get; }

    /// <summary>The answer when the session may not use the connection this way; null for the library.</summary>
    /// <param name="write">Written to: a connection the session only reads answers 403.</param>
    /// <param name="folder">The target folder: the job's right there by the connection's folder rules (404 when
    /// hidden from the session, 403 when only read).</param>
    public async Task<IResult?> CheckAsync(int? connection, bool write, string? folder = null)
    {
        if (connection is not { } id)
            return null;
        var (open, answer) = await _files.OpenAsync(id, _right, write, _cancellationToken);
        if (open is null)
            return answer;
        if (folder is not null)
        {
            if (RemotePaths.Normalize(folder) is not { } target)
                return TypedResults.NotFound();
            if (OpenConnection.AnswerOf(open.Check(target, _right)) is { } refused)
                return refused;
        }
        _capabilities[id] = open.Capabilities;
        var usable = open.Connection;
        _names[id] = $"connection {id} “{usable.Name}” ({usable.Kind}, {ConnectionLevels.Name(usable.Level)})";
        return null;
    }

    /// <summary>How the log and the task history name a checked connection (not its account); null: library.</summary>
    public string? NameOf(int? connection) => connection is { } id ? _names.GetValueOrDefault(id) : null;

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

        var capabilities = _capabilities[id];
        var side = new RemoteTransferSide(id, storage, capabilities, await JobAccessAsync(id, capabilities), _right);
        return (new RemoteTransferEndpoint(side, _transfers.Calls, _transfers.Listings), null);
    }

    /// <summary>The person's access to connection <paramref name="id"/>, read again as the job goes.</summary>
    private async Task<IConnectionJobAccess> JobAccessAsync(int id, ProviderCapabilities capabilities)
    {
        var state = await _files.Current.GetAsync();
        var services = new ConnectionJobServices(_people.Accounts, _people.Rights, _files.Access, _transfers.Changes);
        var who = new ConnectionJobSubject(state.Tenant!, state.User?.Id, id, RemotePaths.ComparisonOf(capabilities));
        return new ConnectionJobAccess(services, who);
    }
}
