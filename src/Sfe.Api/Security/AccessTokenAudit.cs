using System.Security.Claims;

namespace Sfe.Api.Security;

/// <summary>
/// Writes into the server's log every request made with an access token that may change something (any method but
/// GET, HEAD and OPTIONS), with its answer, its account and the token (agents/docs/features/logs.md): what a program
/// did is found again, and told apart from what its account did in the browser. The path only, never the query or
/// the body; never the token's secret.
/// </summary>
public sealed class AccessTokenAudit(RequestDelegate next, ILogger<AccessTokenAudit> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        finally
        {
            if (BearerTokens.TokenOf(context) is not null && ChangesSomething(context.Request))
                Write(context);
        }
    }

    private static bool ChangesSomething(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method);

    private void Write(HttpContext context) =>
        logger.LogInformation(
            "{Method} {Path} answered {Status} to {User}",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            BearerTokens.Describe(context, context.User.FindFirstValue(ClaimTypes.Name))
        );
}
