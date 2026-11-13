using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using ConnectionInfo = Sfe.Core.Connections.ConnectionInfo;

namespace Sfe.Api.Endpoints;

/// <summary>
/// Making a connection by signing in to the provider (<see cref="ConnectionSignInFlow"/>): begin, then the provider's
/// redirect back (<see cref="ConnectionProviderEndpoints.CallbackPath"/>) or the code typed by the person. The right
/// depends on the level: <c>connections.own</c>, <c>connections.library</c>, <c>connections.server</c>.
/// </summary>
public static class ConnectionSignInEndpoints
{
    /// <summary>Where the browser lands in the client after the redirect sign-in (under the app's address).</summary>
    public const string ClientPage = "/connections";

    private static readonly string[] MakeRights =
    [
        Rights.OwnConnections,
        Rights.LibraryConnections,
        Rights.ServerConnections,
    ];

    public static RouteGroupBuilder MapConnectionSignInEndpoints(this RouteGroupBuilder api)
    {
        var connections = api.MapGroup("/connections").WithTags("Connections");

        connections
            .MapPost("/authorize", Authorize)
            .RequireRight(MakeRights)
            .WithName("AuthorizeConnection")
            .WithSummary(
                "Begins making a connection: checks what is asked, then answers the provider's address to send the "
                    + "browser to (and the state it comes back with), or the connection when no more is needed."
            );

        connections
            .MapPost("/authorize/complete", Complete)
            .RequireRight(MakeRights)
            .WithName("CompleteConnectionAuthorization")
            .WithSummary(
                "Ends a sign-in with what the person brings back (the code the provider shows) and makes the "
                    + "connection. A state is used once, by the account that began it, within 10 minutes."
            );

        connections
            .MapGet(ConnectionProviderEndpoints.CallbackPath["/connections".Length..], Callback)
            .RequireRight(MakeRights)
            .WithName("ConnectionOAuthCallback")
            .WithSummary(
                "Where the provider sends the browser back: ends the sign-in begun by this session's account and "
                    + "redirects into the web client (`connections?made=` or `connections?error=`)."
            );

        return api;
    }

    /// <summary>The right to make a connection at <paramref name="level"/>.</summary>
    public static string RightFor(ConnectionLevel level) =>
        level switch
        {
            ConnectionLevel.Account => Rights.OwnConnections,
            ConnectionLevel.Library => Rights.LibraryConnections,
            _ => Rights.ServerConnections,
        };

    private static async Task<
        Results<Ok<SignInBegun>, ValidationProblem, ProblemHttpResult, ForbidHttpResult>
    > Authorize(
        ConnectRequest request,
        HttpRequest http,
        CurrentAccess current,
        ConnectionSignInFlow flow,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        if (state.User is not { } user || state.Tenant is null || !state.Has(RightFor(request.Level)))
            return TypedResults.Forbid();

        var context = new SignInContext(user.Id, state.TenantId, ConnectionProviderEndpoints.ReturnAddress(http));
        var begun = await flow.BeginAsync(request, context, cancellationToken);
        return begun.Status switch
        {
            AccessStatus.Ok => TypedResults.Ok(begun.Value!),
            AccessStatus.Invalid => TypedResults.ValidationProblem(begun.Errors!),
            _ => TypedResults.Problem(begun.Problem, statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<Ok<ConnectionInfo>, ValidationProblem, ProblemHttpResult, NotFound>> Complete(
        SignInCompletionInput input,
        CurrentAccess current,
        ConnectionSignInFlow flow,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        if (state.User is not { } user)
            return TypedResults.NotFound();

        var made = await flow.CompleteAsync(
            input.State,
            user.Id,
            input.Fields ?? new Dictionary<string, string>(),
            level => state.Has(RightFor(level)),
            cancellationToken
        );
        return made.Status switch
        {
            AccessStatus.Ok => TypedResults.Ok(made.Value!),
            AccessStatus.Invalid => TypedResults.ValidationProblem(made.Errors!),
            AccessStatus.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Problem(made.Problem, statusCode: StatusCodes.Status409Conflict),
        };
    }

    /// <summary>
    /// The provider's redirect: <c>code</c> and <c>state</c>, or <c>error</c> when the person refused there. Changes
    /// state on a GET (the provider can only navigate): the state, bound to this session's account and used once, is
    /// the defence against a forged request.
    /// </summary>
    private static async Task<RedirectHttpResult> Callback(
        [AsParameters] OAuthCallback answer,
        CurrentAccess current,
        ConnectionSignInFlow flow,
        CancellationToken cancellationToken
    )
    {
        var (code, state, error, http) = answer;
        var access = await current.GetAsync();
        if (access.User is not { } user)
            return Back(http, "error=signed-out");
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            flow.Abandon(state, user.Id);
            return Back(http, "error=refused");
        }

        var made = await flow.CompleteAsync(
            state,
            user.Id,
            new Dictionary<string, string> { ["code"] = code },
            level => access.Has(RightFor(level)),
            cancellationToken
        );
        return made.Status switch
        {
            AccessStatus.Ok => Back(http, $"made={made.Value!.Id}"),
            AccessStatus.NotFound => Back(http, "error=expired"),
            _ => Back(http, "error=failed"),
        };
    }

    /// <summary>
    /// Into the client, under the app's own address only (never an address from the request's query).
    /// </summary>
    private static RedirectHttpResult Back(HttpRequest http, string query) =>
        TypedResults.Redirect($"{http.PathBase}{ClientPage}?{query}");
}
