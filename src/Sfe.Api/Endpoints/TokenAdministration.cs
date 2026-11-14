using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The access tokens an administrator sees and may revoke (bound with <c>[AsParameters]</c>; none of it is in the
/// OpenAPI document): a server administrator every one; a space's administrator those of the accounts of the spaces
/// where they hold <see cref="Rights.ManageTokens"/>, but never a server administrator's (as for accounts:
/// agents/docs/features/accounts.md).
/// </summary>
internal sealed record TokenAdministration(
    AccessTokens Tokens,
    CurrentAccess Current,
    AccessService Accounts,
    RightsEvaluator Evaluator
)
{
    public async Task<IReadOnlyList<AccessTokenInfo>> VisibleAsync(CancellationToken cancellationToken)
    {
        var user = (await Current.GetAsync()).User!;
        if (user.IsServerAdmin)
            return await Tokens.ListAllAsync(null, cancellationToken);

        var spaces = await ManagedSpacesAsync(user.Id, cancellationToken);
        var tokens = await Tokens.ListAllAsync(spaces, cancellationToken);
        var serverAdmins = await ServerAdministratorsAsync(tokens, cancellationToken);
        return [.. tokens.Where(token => !serverAdmins.Contains(token.UserId))];
    }

    /// <summary>The spaces where <paramref name="userId"/> holds <see cref="Rights.ManageTokens"/>.</summary>
    private async Task<IReadOnlyCollection<int>> ManagedSpacesAsync(int userId, CancellationToken cancellationToken)
    {
        List<int> managed = [];
        foreach (var tenantId in await Evaluator.GetTenantIdsAsync(userId, cancellationToken))
        {
            var access = await Evaluator.GetAsync(new AccessSubject(tenantId, userId), cancellationToken);
            if (access.Has(Rights.ManageTokens))
                managed.Add(tenantId);
        }
        return managed;
    }

    private async Task<HashSet<int>> ServerAdministratorsAsync(
        IEnumerable<AccessTokenInfo> tokens,
        CancellationToken cancellationToken
    )
    {
        HashSet<int> serverAdmins = [];
        foreach (var userId in tokens.Select(token => token.UserId).Distinct())
        {
            if (await Accounts.GetSessionUserAsync(userId, cancellationToken) is { IsServerAdmin: true })
                serverAdmins.Add(userId);
        }
        return serverAdmins;
    }
}
