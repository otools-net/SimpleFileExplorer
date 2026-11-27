using Microsoft.AspNetCore.Authentication;
using Otools.Net.Sfe.Api.Maintenance;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// Checks made before the routes' own rights:
/// <list type="bullet">
/// <item>while the install waits for its update to this version (<see cref="MigrationCoordinator"/>), only the update
/// page's routes answer (503 <c>migration-required</c>): nothing may read or change what the update hasn't brought up
/// yet;</item>
/// <item>until the first administrator exists, only the setup, the installation page (its own code check), the
/// session and the help answer (503 <c>setup-required</c>);</item>
/// <item>once set up, a Local server (<see cref="ServerModes"/>) answers other computers only on the setup, session,
/// installation and update routes (403 <see cref="ServerModes.LocalOnlyType"/>);</item>
/// <item>a request changing something must carry <see cref="Header"/>: a page of another site cannot add it without the
/// browser asking this server first (CORS), which never agrees, so it can't act with a visitor's cookie (a request
/// with an access token needs none);</item>
/// <item>a request with an access token whose token wasn't accepted gets 401, and one calling a route for sessions
/// only 403 (<see cref="SessionOnlyRoutes"/>);</item>
/// <item>the API's documentation pages (served by Swagger UI, not routes) need <see cref="Rights.ApiDocs"/>.</item>
/// </list>
/// </summary>
public sealed class RequestGuard(RequestDelegate next)
{
    public const string Header = "X-SFE-Request";
    public const string SetupRequiredType = ProblemTypes.SetupRequired;
    public const string MigrationRequiredType = ProblemTypes.MigrationRequired;

    public async Task InvokeAsync(
        HttpContext context,
        SetupService setup,
        CurrentAccess current,
        MigrationCoordinator migration,
        IConfiguration configuration
    )
    {
        var path = context.Request.Path;
        var api = ApiRoutes.IsApi(path);
        if (!ApiRoutes.IsApiOrDocument(path))
        {
            await next(context);
            return;
        }

        if (api && LacksRequestHeader(context.Request))
        {
            await TypedResults
                .Problem(
                    $"Requests changing something need the {Header} header.",
                    statusCode: StatusCodes.Status400BadRequest
                )
                .ExecuteAsync(context);
            return;
        }

        await migration.RetryIfUnavailableAsync(context.RequestAborted);
        if (migration.IsBlocking && !ApiRoutes.IsMigration(path))
        {
            await TypedResults
                .Problem(
                    "SFE is waiting for its update: open the app to update it.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    type: MigrationRequiredType
                )
                .ExecuteAsync(context);
            return;
        }

        if (
            !ApiRoutes.IsServerEntry(path)
            && !ApiRoutes.IsHelp(path)
            && await setup.IsRequiredAsync(context.RequestAborted)
        )
        {
            await TypedResults
                .Problem(
                    "The server has no administrator yet: open the app to set it up.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    type: SetupRequiredType
                )
                .ExecuteAsync(context);
            return;
        }

        // A Local server answers its own computer only. The setup, the session (to sign in as the server's
        // administrator), the installation page (with the setup code, to switch to External) and the update page stay
        // open: an install upgraded to Local while used from elsewhere is set to External from there.
        if (
            ServerModes.IsLocal(configuration)
            && !LocalVisitor.IsOnThisComputer(context)
            && !ApiRoutes.IsServerEntry(path)
        )
        {
            await TypedResults
                .Problem(
                    "SFE is in Local mode: it only answers its own computer (http://localhost).",
                    statusCode: StatusCodes.Status403Forbidden,
                    type: ServerModes.LocalOnlyType
                )
                .ExecuteAsync(context);
            return;
        }

        if (BearerTokens.IsBearer(context.Request) && await RefusedBearerAsync(context))
            return;

        if (ApiRoutes.IsDocs(path) && !(await current.GetAsync()).Has(Rights.ApiDocs))
        {
            await (context.User.UserId() is null ? Results.Unauthorized() : Results.Forbid()).ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    /// <summary>
    /// A request changing something without <see cref="Header"/>, unless it comes with an access token: a page of
    /// another site can't make a browser send one, so there is no ambient credential to abuse.
    /// </summary>
    private static bool LacksRequestHeader(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method)
        && !request.Headers.ContainsKey(Header)
        && !BearerTokens.IsBearer(request);

    /// <summary>
    /// Answers a bearer request whose token wasn't accepted (401, whatever the route: never a visitor's rights instead)
    /// or that calls a route for sessions only (403 <see cref="SessionOnlyRoutes.Type"/>); true when it answered.
    /// </summary>
    private static async Task<bool> RefusedBearerAsync(HttpContext context)
    {
        if (context.User.UserId() is null)
        {
            await context.ChallengeAsync(AccessTokenAuthentication.SchemeName);
            return true;
        }

        if (!SessionOnlyRoutes.IsSessionOnly(context.GetEndpoint()))
            return false;
        await TypedResults
            .Problem(
                "This route needs a signed-in session: an access token may not call it.",
                statusCode: StatusCodes.Status403Forbidden,
                type: SessionOnlyRoutes.Type
            )
            .ExecuteAsync(context);
        return true;
    }
}
