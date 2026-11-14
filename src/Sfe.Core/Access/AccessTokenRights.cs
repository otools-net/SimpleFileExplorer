using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// The rights of personal access tokens (agents/docs/development/api-tokens-plan.md), a part of
/// <see cref="RightCatalog.All"/> kept in its own file: the Administrators' only, never a guest's, given to the
/// Administrators of existing spaces once (<see cref="Since"/>). They build their definitions without
/// <see cref="RightCatalog"/>, which reads this list while it initializes.
/// </summary>
internal static class AccessTokenRights
{
    /// <summary>The catalog revision these rights came with.</summary>
    private const int Since = 16;

    public static IReadOnlyList<RightDefinition> All { get; } =
    [
        Space(
            Rights.OwnTokens,
            "Account",
            "Access tokens",
            "Make tokens that let programs use the API as one's own account, with its rights or fewer; see and "
                + "revoke them."
        ),
        Space(
            Rights.ManageTokens,
            "Access",
            "Manage access tokens",
            "See the access tokens of the space's accounts, with their last use, and revoke them."
        ),
    ];

    private static RightDefinition Space(string id, string area, string title, string description) =>
        new(
            id,
            RightScope.Tenant,
            false,
            [BuiltInGroup.Administrators],
            area,
            title,
            description,
            Since,
            RightKind.Write
        );
}
