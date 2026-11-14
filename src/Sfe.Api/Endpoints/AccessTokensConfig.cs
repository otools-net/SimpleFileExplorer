namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>What the client needs to know about personal access tokens (<c>Security:AccessTokens</c>).</summary>
/// <param name="Enabled">Tokens can be made and used.</param>
/// <param name="MaxLifetimeDays">The longest life of a new token, in days; 0: a token may never expire.</param>
public sealed record AccessTokensConfig(bool Enabled, int MaxLifetimeDays);
