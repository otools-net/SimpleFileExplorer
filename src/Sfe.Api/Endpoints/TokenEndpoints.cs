using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;

namespace Sfe.Api.Endpoints;

/// <summary>
/// Personal access tokens (agents/docs/development/api-tokens-plan.md): an account's own (<c>me/tokens</c>) and those
/// administrators see and revoke (<c>access/tokens</c>). Every route needs a signed-in session: a token never makes,
/// lists or revokes tokens (<see cref="SessionOnlyRoutes"/>).
/// </summary>
public static class TokenEndpoints
{
    private const string CreateDescription = """
        The answer holds the whole token (`secret`) once: it is never shown again, only its first characters
        (`prefix`). `expires` (UTC) defaults to the longest life the server allows; `tenantId` limits the token to
        one space the account reaches; `rights` limits it to some of the account's rights (none: all of them, as
        they are at each use). 403 when tokens are turned off, 409 when the account holds as many as allowed.
        """;

    private const string ListAllDescription = """
        Server administrators see every account's tokens; a space's administrators those of the accounts of the
        spaces where they hold `access.tokens`, but no server administrator's.
        """;

    public static RouteGroupBuilder MapTokenEndpoints(this RouteGroupBuilder api)
    {
        MapOwnTokens(api.MapGroup("/me/tokens").WithTags("Access tokens").SessionOnly());

        var all = api.MapGroup("/access/tokens").WithTags("Access tokens").SessionOnly();
        all.MapGet("/", ListAll)
            .RequireRight(Rights.ManageTokens)
            .WithName("GetAccessTokens")
            .WithSummary(
                "The access tokens of the accounts one administers, revoked and expired ones too, newest first."
            )
            .WithDescription(ListAllDescription);
        all.MapDelete("/{id:int}", RevokeAny)
            .RequireRight(Rights.ManageTokens)
            .WithName("RevokeAccessToken")
            .WithSummary("Revokes the access token of an account one administers.");

        return api;
    }

    private static void MapOwnTokens(RouteGroupBuilder own)
    {
        own.MapGet("/", ListOwn)
            .RequireRight(Rights.OwnTokens)
            .WithName("GetOwnAccessTokens")
            .WithSummary("The signed-in account's access tokens, revoked and expired ones too, newest first.");
        own.MapPost("/", Create)
            .RequireRight(Rights.OwnTokens)
            .WithName("CreateOwnAccessToken")
            .WithSummary("Makes an access token for programs calling the API as the signed-in account.")
            .WithDescription(CreateDescription);
        own.MapDelete("/{id:int}", RevokeOwn)
            .RequireRight(Rights.OwnTokens)
            .WithName("RevokeOwnAccessToken")
            .WithSummary("Revokes one of the signed-in account's access tokens: it stops working at once.");
        own.MapPost("/revoke-all", RevokeAllOwn)
            .RequireRight(Rights.OwnTokens)
            .WithName("RevokeAllOwnAccessTokens")
            .WithSummary("Revokes every access token of the signed-in account; answers how many were in use.");
    }

    private static async Task<Ok<IReadOnlyList<AccessTokenInfo>>> ListOwn(
        AccessTokens tokens,
        CurrentAccess current,
        CancellationToken cancellationToken
    )
    {
        var user = (await current.GetAsync()).User!;
        return TypedResults.Ok(await tokens.ListAsync(user.Id, cancellationToken));
    }

    private static async Task<Results<Created<CreatedAccessToken>, ValidationProblem, ProblemHttpResult>> Create(
        AccessTokens tokens,
        CurrentAccess current,
        AccessTokenDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (!tokens.Options.Enabled)
        {
            return TypedResults.Problem(AccessTokens.DisabledProblem, statusCode: StatusCodes.Status403Forbidden);
        }

        var user = (await current.GetAsync()).User!;
        var result = await tokens.CreateAsync(user.Id, draft, cancellationToken);
        return result.Status switch
        {
            AccessStatus.Ok => TypedResults.Created((string?)null, result.Value!),
            AccessStatus.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.Problem(result.Problem, statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<NoContent, NotFound>> RevokeOwn(
        AccessTokens tokens,
        CurrentAccess current,
        int id,
        CancellationToken cancellationToken
    )
    {
        var user = (await current.GetAsync()).User!;
        var status = await tokens.RevokeAsync(id, user.Id, user.Id, cancellationToken);
        return status == AccessStatus.Ok ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Ok<RevokedTokens>> RevokeAllOwn(
        AccessTokens tokens,
        CurrentAccess current,
        CancellationToken cancellationToken
    )
    {
        var user = (await current.GetAsync()).User!;
        return TypedResults.Ok(new RevokedTokens(await tokens.RevokeAllAsync(user.Id, user.Id, cancellationToken)));
    }

    private static async Task<Ok<IReadOnlyList<AccessTokenInfo>>> ListAll(
        [AsParameters] TokenAdministration administration,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await administration.VisibleAsync(cancellationToken));

    private static async Task<Results<NoContent, NotFound>> RevokeAny(
        [AsParameters] TokenAdministration administration,
        int id,
        CancellationToken cancellationToken
    )
    {
        var visible = await administration.VisibleAsync(cancellationToken);
        if (visible.All(token => token.Id != id))
            return TypedResults.NotFound();

        var revokedBy = (await administration.Current.GetAsync()).User!.Id;
        var status = await administration.Tokens.RevokeAsync(id, null, revokedBy, cancellationToken);
        return status == AccessStatus.Ok ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
