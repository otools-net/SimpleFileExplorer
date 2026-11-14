using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// Making a connection, or signing one in again, by signing in to the provider (<see cref="ConnectionSignInFlow"/>):
/// begin, then the provider's redirect back (<see cref="ConnectionProviderEndpoints.CallbackPath"/>) or the code
/// typed by the person. Who may: <see cref="ConnectionManagers.MayManage(AccessState, ConnectionDraft)"/>.
/// </summary>
public static class ConnectionSignInEndpoints
{
    /// <summary>Where the browser lands in the client after the redirect sign-in (under the app's address).</summary>
    public const string ClientPage = "/connections";

    public static RouteGroupBuilder MapConnectionSignInEndpoints(this RouteGroupBuilder api)
    {
        var connections = api.MapGroup("/connections").WithTags("Connections");

        connections
            .MapPost("/authorize", Authorize)
            .RequireRight(ConnectionManagers.Rights)
            .WithName("AuthorizeConnection")
            .WithSummary(
                "Begins making a connection: checks what is asked, then answers the provider's address to send the "
                    + "browser to (and the state it comes back with), or the connection when no more is needed."
            );

        connections
            .MapPost("/{id:int}/reauthorize", Reauthorize)
            .RequireRight(ConnectionManagers.Rights)
            .WithName("ReauthorizeConnection")
            .WithSummary(
                "Begins signing a connection in again (its sign-in was refused or lost): the same connection, groups "
                    + "and rules afterwards. Its owner (account level) or who manages its level."
            );

        connections
            .MapPost("/authorize/complete", Complete)
            .RequireRight(ConnectionManagers.Rights)
            .WithName("CompleteConnectionAuthorization")
            .WithSummary(
                "Ends a sign-in with what the person brings back (the code the provider shows). A state is used once, "
                    + "by the account that began it, within 10 minutes."
            );

        connections
            .MapGet(ConnectionProviderEndpoints.CallbackPath["/connections".Length..], Callback)
            .RequireRight(ConnectionManagers.Rights)
            .WithName("ConnectionOAuthCallback")
            .WithSummary(
                "Where the provider sends the browser back: ends the sign-in begun by this session's account and "
                    + "redirects into the web client (`connections?made=`, `?reconnected=` or `?error=`)."
            );

        return api;
    }

    private static async Task<
        Results<Ok<SignInBegun>, ValidationProblem, ProblemHttpResult, ForbidHttpResult>
    > Authorize(ConnectRequest request, [AsParameters] SignInServices services, CancellationToken cancellationToken)
    {
        var (http, current, flow) = services;
        var state = await current.GetAsync();
        if (state.User is not { } user || state.Tenant is null)
            return TypedResults.Forbid();
        var draft = new ConnectionDraft(
            request.Kind,
            request.Name,
            request.Level,
            user.Id,
            state.TenantId,
            request.RootPath
        );
        if (!ConnectionManagers.MayManage(state, draft))
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

    private static async Task<Results<Ok<SignInBegun>, ValidationProblem, ProblemHttpResult, NotFound>> Reauthorize(
        int id,
        SignInAgain request,
        [AsParameters] SignInServices services,
        CancellationToken cancellationToken
    )
    {
        var (http, current, flow) = services;
        var state = await current.GetAsync();
        if (state.User is not { } user || state.Tenant is null)
            return TypedResults.NotFound();

        var context = new SignInContext(user.Id, state.TenantId, ConnectionProviderEndpoints.ReturnAddress(http));
        var begun = await flow.BeginReconnectAsync(
            id,
            request,
            context,
            draft => ConnectionManagers.MayManage(state, draft),
            cancellationToken
        );
        return begun.Status switch
        {
            AccessStatus.Ok => TypedResults.Ok(begun.Value!),
            AccessStatus.Invalid => TypedResults.ValidationProblem(begun.Errors!),
            AccessStatus.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Problem(begun.Problem, statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<Ok<SignInDone>, ValidationProblem, ProblemHttpResult, NotFound>> Complete(
        SignInCompletionInput input,
        CurrentAccess current,
        ConnectionSignInFlow flow,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        if (state.User is not { } user)
            return TypedResults.NotFound();

        var done = await flow.CompleteAsync(
            input.State,
            user.Id,
            input.Fields ?? new Dictionary<string, string>(),
            draft => ConnectionManagers.MayManage(state, draft),
            cancellationToken
        );
        return done.Status switch
        {
            AccessStatus.Ok => TypedResults.Ok(done.Value!),
            AccessStatus.Invalid => TypedResults.ValidationProblem(done.Errors!),
            AccessStatus.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Problem(done.Problem, statusCode: StatusCodes.Status409Conflict),
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

        var done = await flow.CompleteAsync(
            state,
            user.Id,
            new Dictionary<string, string> { ["code"] = code },
            draft => ConnectionManagers.MayManage(access, draft),
            cancellationToken
        );
        return done.Status switch
        {
            AccessStatus.Ok => Back(http, Landing(done.Value!)),
            AccessStatus.NotFound => Back(http, "error=expired"),
            _ => Back(http, "error=failed"),
        };
    }

    /// <summary>What the client's page is told of a sign-in ended.</summary>
    private static string Landing(SignInDone done) =>
        done switch
        {
            { Reconnected: true, OtherAccount: true } => $"reconnected={done.Connection.Id}&otherAccount=true",
            { Reconnected: true } => $"reconnected={done.Connection.Id}",
            _ => $"made={done.Connection.Id}",
        };

    /// <summary>
    /// Into the client, under the app's own address only (never an address from the request's query).
    /// </summary>
    private static RedirectHttpResult Back(HttpRequest http, string query) =>
        TypedResults.Redirect($"{http.PathBase}{ClientPage}?{query}");
}
