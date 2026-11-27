using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What a route on a connection's files uses (bound with <c>[AsParameters]</c>), and the one way it opens the
/// connection: usable by the session (else 404), the route's right held, the provider's sign-in still good; with
/// where the session may use its rights in it (<see cref="OpenConnection.Folders"/>), which every route checks its
/// paths against before calling the provider.
/// </summary>
internal sealed record ConnectionFileServices(
    CurrentAccess Current,
    ConnectionAccess Access,
    ConnectionService Connections,
    ConnectionProviders Providers,
    ConnectionGates Gates,
    RemoteReadOnlyFolders ReadOnlyFolders,
    Microsoft.Extensions.Options.IOptionsMonitor<Otools.Net.Sfe.Core.Options.SfeOptions> ServerOptions,
    HttpContext Context
)
{
    /// <summary>What the session may do in <paramref name="open"/> (agents/docs/features/item-access.md).</summary>
    public async Task<ConnectionAccessReport> AccessReportAsync(OpenConnection open) =>
        new(await Current.GetAsync(), ServerOptions.CurrentValue.Rights.Disabled, open);

    /// <summary>
    /// The provider refuses changes in <paramref name="folder"/> (<see cref="RemoteReadOnlyFolders"/>), asked with
    /// <paramref name="storage"/> in the turn the caller holds; false for providers that don't tell.
    /// </summary>
    public Task<bool> ProviderRefusesAsync(
        OpenConnection open,
        string folder,
        IRemoteStorage storage,
        CancellationToken cancellationToken
    ) =>
        open.Capabilities.ReadOnlyItems
            ? ReadOnlyFolders.IsReadOnlyAsync(
                open.Connection.Id,
                folder,
                () => storage.GetAsync(folder, cancellationToken)
            )
            : Task.FromResult(false);

    /// <summary>
    /// <see cref="ProviderRefusesAsync(OpenConnection, string, IRemoteStorage, CancellationToken)"/> outside a turn:
    /// one is taken when the answer isn't kept; false while the provider asks to wait.
    /// </summary>
    public Task<bool> ProviderRefusesAsync(OpenConnection open, string folder, CancellationToken cancellationToken)
    {
        var id = open.Connection.Id;
        if (!open.Capabilities.ReadOnlyItems || Gates.WaitLeft(id) is not null)
            return Task.FromResult(false);
        return ReadOnlyFolders.IsReadOnlyAsync(id, folder, async () =>
        {
            using var turn = await Gates.EnterAsync(id, cancellationToken);
            return turn is null ? null : await open.Storage.GetAsync(folder, cancellationToken);
        });
    }

    /// <summary>
    /// The connection opened for this request (its storage disposed when the answer is sent, after a file is
    /// streamed), or the answer to give instead.
    /// </summary>
    public Task<(OpenConnection? Open, IResult? Answer)> OpenAsync(
        int id,
        string right,
        CancellationToken cancellationToken
    ) => OpenAsync(id, right, write: false, cancellationToken);

    /// <inheritdoc cref="OpenAsync(int, string, CancellationToken)"/>
    /// <param name="write">The route changes files: a connection where the session may use
    /// <paramref name="right"/> nowhere (read only, Read for all its groups) answers 403.</param>
    public async Task<(OpenConnection? Open, IResult? Answer)> OpenAsync(
        int id,
        string right,
        bool write,
        CancellationToken cancellationToken
    )
    {
        var state = await Current.GetAsync();
        var subject = ConnectionEndpoints.SubjectOf(state);
        var usable = await Access.FindAsync(subject, id, cancellationToken);
        if (usable is null || Providers.Find(usable.Kind) is not { } provider)
            return (null, TypedResults.NotFound());
        if (!state.Has(right))
            return (null, TypedResults.Forbid());
        var comparison = RemotePaths.ComparisonOf(provider.Capabilities);
        var folders = await Access.FoldersAsync(subject, id, (state.Access.Rights, comparison), cancellationToken);
        if (write && !folders.AllowsSomewhere(right))
            return (null, TypedResults.Forbid());

        var opened = await Connections.OpenAsync(id, cancellationToken);
        if (opened.Value is not { } storage)
        {
            return (
                null,
                opened.Status == Otools.Net.Sfe.Core.Access.AccessStatus.NotFound
                    ? TypedResults.NotFound()
                    : ConnectionProblems.OfOpening(opened.Problem)
            );
        }
        Context.Response.RegisterForDisposeAsync(storage);
        return (new OpenConnection(usable, storage, provider.Capabilities, folders), null);
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

    /// <summary>
    /// Runs work that calls the provider in turns of its own (the thumbnails' batches, the content cache's
    /// downloads): the provider's errors and the cache's refusals as their answers.
    /// </summary>
    public async Task<IResult> CatchAsync(OpenConnection open, Func<Task<IResult>> work)
    {
        try
        {
            return await work();
        }
        catch (RemoteStorageException error)
        {
            return await ConnectionProblems.OfAsync(error, open.Connection.Id, Context, Connections);
        }
        catch (Core.Connections.Previews.ConnectionCacheException refused)
        {
            return ConnectionCacheProblems.Of(refused, Context);
        }
    }
}
