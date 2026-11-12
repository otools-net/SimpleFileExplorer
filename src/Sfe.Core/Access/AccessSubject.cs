namespace Sfe.Core.Access;

/// <summary>Who asks, in which library: an account, or a visitor without one (<see cref="UserId"/> null).</summary>
public sealed record AccessSubject(int TenantId, int? UserId);
