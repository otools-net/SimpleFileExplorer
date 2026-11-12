namespace Sfe.Api.Security;

/// <summary>A zip asked for, waiting for the browser to download it by its link.</summary>
public sealed record PendingArchive(int TenantId, string Caller, IReadOnlyList<string> Paths, DateTime Expires);
