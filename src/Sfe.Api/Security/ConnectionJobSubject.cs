using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <summary>Who runs a job in a connection, where (<see cref="ConnectionJobAccess"/>).</summary>
/// <param name="UserId">The account that started it; null for a visitor (never any connection).</param>
/// <param name="Comparison">How the connection's provider compares names.</param>
public sealed record ConnectionJobSubject(
    TenantInfo Tenant,
    int? UserId,
    int ConnectionId,
    StringComparison Comparison
);
