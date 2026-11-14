namespace Otools.Net.Sfe.Api.Security;

/// <summary>A zip asked for, waiting for the browser to download it by its link.</summary>
/// <param name="Scope">Where its paths are: null for the library, <c>connection:{id}</c> for a connection's
/// files.</param>
public sealed record PendingArchive(
    int TenantId,
    string Caller,
    IReadOnlyList<string> Paths,
    DateTime Expires,
    string? Scope = null
);
