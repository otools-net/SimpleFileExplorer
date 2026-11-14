using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="DisabledByServer">Rights the server switches off for everyone (<c>Sfe:Rights:Disabled</c>).</param>
/// <param name="DisabledInTenant">Rights switched off in the space asked about by a server administrator.</param>
public sealed record RightsCatalogInfo(
    IReadOnlyList<RightDefinition> Rights,
    IReadOnlyList<string> DisabledByServer,
    IReadOnlyList<string> DisabledInTenant
);

/// <param name="AnonymousGuestsAllowed">The server lets spaces open to visitors without an account
/// (<c>Sfe:Security:AnonymousGuests</c>).</param>
public sealed record TenantAccessInfo(
    int Id,
    string Name,
    bool GuestAccess,
    bool AnonymousGuestsAllowed,
    IReadOnlyList<string> DisabledRights
);

public sealed record GuestAccessChange(bool GuestAccess);
