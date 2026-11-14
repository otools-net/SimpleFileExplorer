namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// Routes a personal access token may not call (agents/docs/development/api-tokens-plan.md): what lets someone into
/// an account or the server (sign-in, password, setup, installation, update) and the tokens themselves. A bearer
/// request gets 403 <see cref="Type"/> there (<see cref="RequestGuard"/>): a leaked token never makes more tokens
/// nor takes its account over.
/// </summary>
public static class SessionOnlyRoutes
{
    public const string Type = ProblemTypes.SessionOnly;

    /// <summary>Marks the route (or every route of a group) as callable with a signed-in session only.</summary>
    public static TBuilder SessionOnly<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.WithMetadata(new SessionOnlyMetadata());

    /// <summary>The route needs a session (<see cref="SessionOnly{TBuilder}"/>).</summary>
    public static bool IsSessionOnly(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<SessionOnlyMetadata>() is not null;
}
