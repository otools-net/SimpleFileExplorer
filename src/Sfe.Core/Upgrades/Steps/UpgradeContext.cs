using Microsoft.Extensions.Logging;

namespace Sfe.Core.Upgrades.Steps;

/// <summary>What an <see cref="IUpgradeStep"/> works on.</summary>
/// <param name="CacheDirectories">The thumbnail cache folder of every library (disabled ones included), full paths.</param>
/// <param name="Note">Adds a line to the update page's log.</param>
public sealed record UpgradeContext(IReadOnlyList<string> CacheDirectories, Action<string> Note, ILogger Logger);
