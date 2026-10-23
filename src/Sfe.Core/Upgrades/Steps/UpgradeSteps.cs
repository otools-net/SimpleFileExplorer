using Sfe.Core.Upgrades.Steps.V1_40_0;
using Sfe.Core.Upgrades.Steps.V1_47_0;
using Sfe.Core.Upgrades.Steps.V1_82_0;

namespace Sfe.Core.Upgrades.Steps;

public static class UpgradeSteps
{
    /// <summary>Every step, by version (the order they run in).</summary>
    public static IReadOnlyList<IUpgradeStep> All { get; } =
    [
        new ShardCacheLayout(),
        new RenewSetupCode(),
        new KeepWorkFolderInLibraries(),
    ];

    /// <summary>
    /// The steps an install of <paramref name="installed"/> needs to become <paramref name="app"/>: those after it, up to
    /// the running version. Null = unknown (before 1.37.0, or not tracked): every step.
    /// </summary>
    public static IReadOnlyList<IUpgradeStep> Due(SemanticVersion? installed, SemanticVersion app) =>
        [.. All.Where(step => (installed is null || step.Version > installed) && step.Version <= app).OrderBy(step => step.Version)];
}
