using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <summary>
/// Checks made before the routes' own rights:
/// <list type="bullet">
/// <item>until the first administrator exists, only the setup and the session answer (503 <c>setup-required</c>);</item>
/// <item>a request changing something must carry <see cref="Header"/>: a page of another site cannot add it without the
/// browser asking this server first (CORS), which never agrees, so it can't act with a visitor's cookie;</item>
/// <item>the API's documentation pages (served by Swagger UI, not routes) need <see cref="Rights.ApiDocs"/>.</item>
/// </list>
/// </summary>
public sealed class RequestGuard(RequestDelegate next)
{
    public const string Header = "X-SFE-Request";
    public const string SetupRequiredType = "setup-required";

    public async Task InvokeAsync(HttpContext context, SetupService setup, CurrentAccess current)
    {
        var path = context.Request.Path;
        var api = path.StartsWithSegments("/api");
        if (!api && !path.StartsWithSegments("/openapi"))
        {
            await next(context);
            return;
        }

        if (api && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method) && !context.Request.Headers.ContainsKey(Header))
        {
            await TypedResults.Problem($"Requests changing something need the {Header} header.", statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
            return;
        }

        if (!path.StartsWithSegments("/api/setup") && !path.StartsWithSegments("/api/session") && await setup.IsRequiredAsync(context.RequestAborted))
        {
            await TypedResults.Problem(
                    "The server has no administrator yet: open the app to set it up.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    type: SetupRequiredType)
                .ExecuteAsync(context);
            return;
        }

        if (path.StartsWithSegments("/api/docs") && !(await current.GetAsync()).Has(Rights.ApiDocs))
        {
            await (context.User.UserId() is null ? Results.Unauthorized() : Results.Forbid()).ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
