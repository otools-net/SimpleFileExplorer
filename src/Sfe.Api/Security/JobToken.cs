using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// The access token a job was started with (agents/docs/features/access-tokens.md): the job checks each item
/// with the token's rights over its account's, as the request did, and stops once the token is revoked or expired.
/// A job started from a session has none.
/// </summary>
public sealed record JobToken(PresentedAccessToken Token, AccessTokens Tokens)
{
    /// <summary>What the items a job didn't do any more say.</summary>
    public const string StoppedProblem =
        "The access token that started this job was revoked or has expired: the rest was not done.";

    /// <summary>The token still works.</summary>
    public Task<bool> IsActiveAsync(CancellationToken cancellationToken = default) =>
        Tokens.IsActiveAsync(Token.Id, cancellationToken);

    /// <summary>The account's access narrowed to the token's rights.</summary>
    public EffectiveAccess Limit(EffectiveAccess access) => BearerTokens.Limited(access, Token);
}
