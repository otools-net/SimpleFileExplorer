using Sfe.Api.Security;
using Sfe.Core.Connections;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What a route on a connection's files uses (bound with <c>[AsParameters]</c>), and the one way it opens the
/// connection: usable by the session (else 404), the route's right held, the provider's sign-in still good.
/// </summary>
internal sealed record ConnectionFileServices(
    CurrentAccess Current,
    ConnectionAccess Access,
    ConnectionService Connections,
    ConnectionProviders Providers,
    ConnectionGates Gates,
    HttpContext Context
)
{
    /// <summary>
    /// The connection opened for this request (its storage disposed when the answer is sent, after a file is
    /// streamed), or the answer to give instead.
    /// </summary>
    public async Task<(OpenConnection? Open, IResult? Answer)> OpenAsync(
        int id,
        string right,
        CancellationToken cancellationToken
    )
    {
        var state = await Current.GetAsync();
        var usable = await Access.FindAsync(ConnectionEndpoints.SubjectOf(state), id, cancellationToken);
        if (usable is null || Providers.Find(usable.Kind) is not { } provider)
            return (null, TypedResults.NotFound());
        if (!state.Has(right))
            return (null, TypedResults.Forbid());

        var opened = await Connections.OpenAsync(id, cancellationToken);
        if (opened.Value is not { } storage)
        {
            return (
                null,
                opened.Status == Sfe.Core.Access.AccessStatus.NotFound
                    ? TypedResults.NotFound()
                    : ConnectionProblems.OfOpening(opened.Problem)
            );
        }
        Context.Response.RegisterForDisposeAsync(storage);
        return (new OpenConnection(usable, storage, provider.Capabilities), null);
    }

    /// <summary>
    /// Runs a call on the connection's files in its turn (<see cref="ConnectionGates"/>); the provider's errors as
    /// their answers. While the provider asks to wait, answers 429 without calling it.
    /// </summary>
    public async Task<IResult> RunAsync(OpenConnection open, Func<IRemoteStorage, Task<IResult>> call)
    {
        var id = open.Connection.Id;
        if (Gates.WaitLeft(id) is { } left)
            return ConnectionProblems.Waiting(left, Context);
        using var turn = await Gates.EnterAsync(id, Context.RequestAborted);
        if (turn is null)
            return ConnectionProblems.Waiting(TimeSpan.FromSeconds(1), Context);
        try
        {
            return await call(open.Storage);
        }
        catch (RemoteStorageException error)
        {
            if (error.Kind == RemoteErrorKind.RateLimited)
                Gates.Wait(id, error.RetryAfter);
            return await ConnectionProblems.OfAsync(error, id, Context, Connections);
        }
    }
}
