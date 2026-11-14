using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Library;

namespace Sfe.Api.Endpoints;

/// <summary>
/// The folder rules a library sets in a connection it reaches (its own, or the server's offered to it): who of its
/// groups and accounts may see or change each folder of the connection. For those who manage the library's
/// connections (<see cref="Rights.LibraryConnections"/>), who see every folder of it, whatever rules apply to
/// themselves. The answers have the shapes of the library's own (<see cref="FolderRuleEndpoints"/>).
/// </summary>
public static class ConnectionFolderRuleEndpoints
{
    private const string ManagersSeeAll =
        "folder rules of a connection are managed for the request's library: managers see every folder of it; "
        + "404 for a connection the library doesn't reach";

    /// <summary>Most folders listed: a picker, not a listing.</summary>
    private const int MaxPickerFolders = 1000;

    /// <summary>Most pages of the provider read for the picker (each a call to the provider).</summary>
    private const int MaxPickerPages = 20;

    private static readonly string SetRulesDescription = $"""
        The body lists every rule of the request's library in the connection: those left out are removed. A group
        uses the connection only with its level given in `PUT …/groups`; a rule may then hide a folder or change its
        level there. At most {ConnectionRuleService.MaxRules} rules; none on an account's connection.
        """;

    public static RouteGroupBuilder MapConnectionFolderRuleEndpoints(this RouteGroupBuilder api)
    {
        var connection = api.MapGroup("/connections/{id:int}").WithTags("Connections");

        connection
            .MapGet("/folder-rules", List)
            .RequireRight(Rights.LibraryConnections)
            .WithName("GetConnectionFolderRules")
            .WithSummary("The request's library's folder rules in a connection.");

        connection
            .MapPut("/folder-rules", Set)
            .RequireRight(Rights.LibraryConnections)
            .FolderScoped(ManagersSeeAll)
            .WithName("SetConnectionFolderRules")
            .WithSummary("Replaces the request's library's folder rules in a connection; answers them.")
            .WithDescription(SetRulesDescription);

        connection
            .MapGet("/folders", Folders)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.LibraryConnections)
            .FolderScoped(ManagersSeeAll)
            .WithName("GetConnectionFolders")
            .WithSummary(
                "The sub-folders of a folder of a connection, for choosing where a rule goes (every folder, "
                    + "whatever rules apply to the asker)."
            );

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<ConnectionFolderRuleView>>, NotFound>> List(
        int id,
        [AsParameters] ConnectionFolderRuleServices services,
        CancellationToken cancellationToken
    )
    {
        if (await services.ReachedAsync(id, cancellationToken) is not { State: var state })
            return TypedResults.NotFound();
        return TypedResults.Ok(await services.ViewsAsync(id, state.TenantId, cancellationToken));
    }

    private static async Task<Results<Ok<IReadOnlyList<ConnectionFolderRuleView>>, ValidationProblem, NotFound>> Set(
        int id,
        [AsParameters] ConnectionFolderRuleServices services,
        ConnectionFolderRulesInput input,
        CancellationToken cancellationToken
    )
    {
        if (await services.ReachedAsync(id, cancellationToken) is not { State: var state })
            return TypedResults.NotFound();

        var result = await services.Rules.SetAsync(
            id,
            state.TenantId,
            input.Rules ?? [],
            state.User?.Id,
            cancellationToken
        );
        return result.Status switch
        {
            AccessStatus.Ok => TypedResults.Ok(
                await services.ViewsOfAsync(result.Value!, state.TenantId, cancellationToken)
            ),
            AccessStatus.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<IResult> Folders(
        int id,
        [AsParameters] ConnectionFolderRuleServices services,
        ConnectionGates gates,
        string? path = null,
        CancellationToken cancellationToken = default
    )
    {
        if (RemotePaths.Normalize(path) is not { } folder || await services.ReachedAsync(id, cancellationToken) is null)
            return TypedResults.NotFound();
        if (gates.WaitLeft(id) is { } left)
            return ConnectionProblems.Waiting(left, services.Context);

        var opened = await services.Connections.OpenAsync(id, cancellationToken);
        if (opened.Value is not { } storage)
        {
            return opened.Status == AccessStatus.NotFound
                ? TypedResults.NotFound()
                : ConnectionProblems.OfOpening(opened.Problem);
        }
        await using (storage)
        {
            using var turn = await gates.EnterAsync(id, cancellationToken);
            if (turn is null)
                return ConnectionProblems.Waiting(TimeSpan.FromSeconds(1), services.Context);
            try
            {
                return TypedResults.Ok(await PickerAsync(storage, folder, cancellationToken));
            }
            catch (RemoteStorageException error)
            {
                if (error.Kind == RemoteErrorKind.RateLimited)
                    gates.Wait(id, error.RetryAfter);
                return await ConnectionProblems.OfAsync(error, id, services.Context, services.Connections);
            }
        }
    }

    /// <summary>
    /// The sub-folders of <paramref name="folder"/>, in natural order: those of the first
    /// <see cref="MaxPickerPages"/> pages of the provider, <see cref="MaxPickerFolders"/> at most.
    /// </summary>
    private static async Task<FolderPicker> PickerAsync(
        IRemoteStorage storage,
        string folder,
        CancellationToken cancellationToken
    )
    {
        if (folder.Length > 0 && await storage.GetAsync(folder, cancellationToken) is not { IsFolder: true })
            throw new RemoteStorageException(RemoteErrorKind.NotFound, "No such folder.");

        var names = new List<string>();
        string? cursor = null;
        for (var page = 0; page < MaxPickerPages && names.Count < MaxPickerFolders; page++)
        {
            var listed = await storage.ListAsync(folder, cursor, cancellationToken);
            names.AddRange(listed.Items.Where(i => i.IsFolder).Select(i => i.Name));
            if ((cursor = listed.Cursor) is null)
                break;
        }

        var folders = names
            .Order(NaturalComparer.Instance)
            .Take(MaxPickerFolders)
            .Select(name => new FolderChoice(name, RemotePaths.Combine(folder, name)))
            .ToList();
        var parent = folder.Length == 0 ? null : RemotePaths.Parent(folder);
        return new FolderPicker(folder, parent, folders);
    }
}
