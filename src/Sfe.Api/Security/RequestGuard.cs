using Sfe.Api.Maintenance;
using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <summary>
/// Checks made before the routes' own rights:
/// <list type="bullet">
/// <item>while the install waits for its update to this version (<see cref="MigrationCoordinator"/>), only the update
/// page's routes answer (503 <c>migration-required</c>): nothing may read or change what the update hasn't brought up
/// yet;</item>
/// <item>until the first administrator exists, only the setup, the installation page (its own code check) and the
/// session answer (503 <c>setup-required</c>);</item>
/// <item>once set up, a Local server (<see cref="ServerModes"/>) answers other computers only on the setup, session,
/// installation and update routes (403 <see cref="ServerModes.LocalOnlyType"/>);</item>
/// <item>a request changing something must carry <see cref="Header"/>: a page of another site cannot add it without the
/// browser asking this server first (CORS), which never agrees, so it can't act with a visitor's cookie;</item>
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

        if (
            api
            && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method)
            && !context.Request.Headers.ContainsKey(Header)
        )
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

        if (!ApiRoutes.IsServerEntry(path) && await setup.IsRequiredAsync(context.RequestAborted))
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

        if (ApiRoutes.IsDocs(path) && !(await current.GetAsync()).Has(Rights.ApiDocs))
        {
            await (context.User.UserId() is null ? Results.Unauthorized() : Results.Forbid()).ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
