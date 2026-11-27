using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The getting-started checklist of the administration page: the first things to set up in a space, each with
/// whether it is done (<see cref="GettingStarted"/>). The client names the items and links them to their pages.
/// </summary>
public static class GettingStartedEndpoints
{
    private const string Description = """
        Items, in order: `library` (the space's library folder is there; `server.settings`), `groups` (a group made
        by an administrator; `tenant.members`), `folders` (a folder rule; `tenant.members`), `accounts` (another
        account in the space; `tenant.members` or `server.users`), `connection` (a connection the space can see:
        its own, one the server offers it, or the account's; `connections.library` or `connections.server`). Only
        those whose right the session holds are listed.
        """;

    public static RouteGroupBuilder MapGettingStartedEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/access/getting-started", Get)
            .RequireRight([.. GettingStarted.Items.SelectMany(item => item.Rights).Distinct()])
            .WithTags("Access")
            .WithName("GetGettingStarted")
            .WithSummary("The first things to set up in the space, and which of them are done.")
            .WithDescription(Description);

        return api;
    }

    private static async Task<Results<Ok<GettingStartedView>, UnauthorizedHttpResult>> Get(
        CurrentAccess current,
        GettingStarted gettingStarted,
        LibraryPaths paths,
        CancellationToken cancellationToken
    )
    {
        if (await current.GetAsync() is not { User: { } user } state)
            return TypedResults.Unauthorized();
        var space = (state.TenantId, user.Id, Directory.Exists(paths.Root));
        var items = await gettingStarted.ItemsAsync(space, state.Has, cancellationToken);
        return TypedResults.Ok(new GettingStartedView(items));
    }
}
