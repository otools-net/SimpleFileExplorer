namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// Personal access tokens (<c>Sfe:Security:AccessTokens</c>): programs calling the API as an account
/// (<see cref="Access.AccessTokens"/>). Server settings, lockable from the configuration.
/// </summary>
public sealed class AccessTokenOptions
{
    public const int MaxLifetimeDaysLimit = 3650;
    public const int MaxPerAccountLimit = 1000;

    /// <summary>Off: no token is made, and those already made don't work (they are kept).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Longest life of a new token, in days; 0 = a token may never expire.</summary>
    public int MaxLifetimeDays { get; set; } = 365;

    /// <summary>Tokens an account may hold at once (those not revoked nor expired).</summary>
    public int MaxPerAccount { get; set; } = 20;
}
