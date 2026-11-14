namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>What "revoke all" did.</summary>
/// <param name="Revoked">The tokens that were in use until then (neither revoked nor expired before).</param>
public sealed record RevokedTokens(int Revoked);
