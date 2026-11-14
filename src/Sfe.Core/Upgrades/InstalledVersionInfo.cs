namespace Otools.Net.Sfe.Core.Upgrades;

/// <param name="Exists">The database exists and has tables: an install, set up by some version of SFE.</param>
/// <param name="Version">The version that last set it up or updated it; null when it predates the record (before
/// 1.37.0).</param>
public sealed record InstalledVersionInfo(bool Exists, string? Version);
