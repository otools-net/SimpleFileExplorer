using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// Requests made with a personal access token (<c>Authorization: Bearer sfe_…</c>): how they are told apart, and the
/// token one was authenticated with (<see cref="AccessTokenAuthentication"/>). A bearer request is the token's alone:
/// its cookies are ignored and it needs no <see cref="RequestGuard.Header"/> (nothing a page of another site could
/// make a browser send).
/// </summary>
public static class BearerTokens
{
    private const string Prefix = "Bearer ";

    /// <summary>The request sends <c>Authorization: Bearer …</c>: the token decides who asks, not the cookie.</summary>
    public static bool IsBearer(HttpRequest request) => Presented(request) is not null;

    /// <summary>What follows <c>Bearer</c> in the request's Authorization header; null without one.</summary>
    public static string? Presented(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? header[Prefix.Length..].Trim() : null;
    }

    /// <summary>The token the request was authenticated with; null for a session or a visitor.</summary>
    public static PresentedAccessToken? TokenOf(HttpContext context) => context.Features.Get<PresentedAccessToken>();

    /// <summary>
    /// <paramref name="access"/> narrowed to the rights <paramref name="token"/> lists (none listed: unchanged): a
    /// token never has a right its account lacks now, nor one it wasn't made with.
    /// </summary>
    public static EffectiveAccess Limited(EffectiveAccess access, PresentedAccessToken? token) =>
        token?.Rights is { } limits
            ? access with
            {
                Rights = access.Rights.Where(limits.Contains).ToHashSet(StringComparer.Ordinal),
            }
            : access;

    /// <summary>
    /// Who asks, for the log: the account's name, followed by the token when the request came with one, so that
    /// what a program changed is told apart from what its account did (agents/docs/features/logs.md).
    /// </summary>
    public static string Describe(HttpContext context, string? userName)
    {
        var who = userName ?? "a visitor";
        return TokenOf(context) is { } token ? $"{who} through access token {token.Id} “{token.Name}”" : who;
    }
}
