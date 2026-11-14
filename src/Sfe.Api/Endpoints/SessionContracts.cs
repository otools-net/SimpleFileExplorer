using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Local">A local account: its password can be changed here (a directory account's is changed in the
/// directory).</param>
public sealed record SessionAccount(
    int Id,
    string UserName,
    string? DisplayName,
    UserKind Kind,
    bool IsServerAdmin,
    bool Local
);

public sealed record SessionTenant(int Id, string Slug, string Name);

/// <param name="User">The signed-in account; null for a visitor without one.</param>
/// <param name="Tenant">The library the session works in.</param>
/// <param name="Tenants">The libraries the account can switch to.</param>
/// <param name="GuestTenants">Libraries open to visitors without an account (listed on the sign-in page).</param>
/// <param name="SetupRequired">No administrator yet: only the setup page works.</param>
/// <param name="SetupAvailable">The setup page can be used (also to recover an administrator,
/// <c>Sfe:Setup:Recover</c>).</param>
/// <param name="CanBrowse">The library can be browsed in this session (otherwise the client shows the sign-in
/// page).</param>
public sealed record SessionInfo(
    SessionAccount? User,
    SessionTenant? Tenant,
    IReadOnlyList<SessionTenant> Tenants,
    IReadOnlyList<SessionTenant> GuestTenants,
    bool SetupRequired,
    bool SetupAvailable,
    bool CanBrowse
);

public sealed record SignInRequest(string? UserName, string? Password, bool Remember = false);

/// <param name="Tenant">The library's short name (<see cref="SessionTenant.Slug"/>).</param>
public sealed record TenantChoice(string? Tenant);

public sealed record PasswordChange(string? Current, string? Password);

public sealed record RecoveryRequest(string? UserName);

/// <param name="Minutes">How long a code works.</param>
public sealed record RecoveryRequested(int Minutes);

/// <param name="Code">The code the server showed in its console (or its recovery file).</param>
public sealed record RecoveryCompletion(string? UserName, string? Code, string? Password, bool Remember = false);
