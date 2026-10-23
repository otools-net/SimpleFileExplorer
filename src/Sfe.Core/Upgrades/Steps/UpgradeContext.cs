using Microsoft.Extensions.Logging;

namespace Sfe.Core.Upgrades.Steps;

/// <summary>What an <see cref="IUpgradeStep"/> works on.</summary>
/// <param name="CacheDirectories">The thumbnail cache folder of every library (disabled ones included), full paths.</param>
/// <param name="Note">Adds a line to the update page's log.</param>
/// <param name="GeneratedCode">The setup code the server makes itself; null when the caller has none (tests).</param>
/// <param name="Settings">The settings by level; null when the caller has none (tests).</param>
/// <param name="TenantIds">Every library (disabled ones included).</param>
public sealed record UpgradeContext(
    IReadOnlyList<string> CacheDirectories,
    Action<string> Note,
    ILogger Logger,
    Access.GeneratedSetupCode? GeneratedCode = null,
    Settings.SettingLayers? Settings = null,
    IReadOnlyList<int>? TenantIds = null);
