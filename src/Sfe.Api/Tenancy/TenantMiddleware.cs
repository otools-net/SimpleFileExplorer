using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Api.Tenancy;

/// <summary>
/// Finds the services of the request's library. A client that names the library it shows (<see cref="Header"/>) gets
/// 409 <c>tenant-changed</c> when the session is in another one by now (switched in another tab): it reloads. A
/// request with an access token picks its library with that header instead (<see cref="CurrentAccess"/>).
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public const string Header = "X-SFE-Tenant";
    public const string TenantChangedType = ProblemTypes.TenantChanged;

    public async Task InvokeAsync(HttpContext context, TenantScope scope, CurrentAccess current)
    {
        var path = context.Request.Path;
        if (!ApiRoutes.IsApiOrDocument(path) || ApiRoutes.IsServerEntry(path) || ApiRoutes.IsDocs(path))
        {
            await next(context);
            return;
        }

        var state = await current.GetAsync();
        // A task started by this request remembers who to tell when it ends.
        TaskStarter.Current = state.User?.Id;
        if (
            context.Request.Headers.TryGetValue(Header, out var shown)
            && int.TryParse(shown, out var shownId)
            && shownId != state.TenantId
        )
        {
            await WrongTenant(context).ExecuteAsync(context);
            return;
        }

        if (await scope.InitializeAsync(context.RequestAborted) is not { } host)
        {
            await TypedResults
                .Problem("This space is not available.", statusCode: StatusCodes.Status503ServiceUnavailable)
                .ExecuteAsync(context);
            return;
        }

        // The routes' Core services come from the library's own container; its singletons are not the request's to
        // dispose.
        var original = context.RequestServices;
        context.RequestServices = new TenantRequestServices(original, host.Services);
        try
        {
            await next(context);
        }
        finally
        {
            context.RequestServices = original;
        }
    }

    /// <summary>
    /// A session switched to another space meanwhile (409: the page reloads); a request with an access token named a
    /// space its token isn't for, or its account doesn't reach (403: nothing to reload).
    /// </summary>
    private static IResult WrongTenant(HttpContext context) =>
        BearerTokens.IsBearer(context.Request)
            ? TypedResults.Problem(
                "The token or its account doesn't reach that space.",
                statusCode: StatusCodes.Status403Forbidden
            )
            : TypedResults.Problem(
                "The session is in another space now: reload the page.",
                statusCode: StatusCodes.Status409Conflict,
                type: TenantChangedType
            );
}
