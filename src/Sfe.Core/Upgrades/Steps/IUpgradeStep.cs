namespace Sfe.Core.Upgrades.Steps;

/// <summary>
/// A change an update makes outside the databases' schemas (EF migrations and <c>DatabaseInitializer</c> do those),
/// e.g. a new layout of the caches. Each step lives in the folder of the version that needs it
/// (<c>Upgrades/Steps/V1_40_0/</c>) and is listed in <see cref="UpgradeSteps.All"/>.
/// </summary>
/// <remarks>
/// Steps run during the confirmed update, before any library starts (no background job writes meanwhile), for an
/// install of an earlier version than <see cref="Version"/>. They must be idempotent: an update that failed is run
/// again, and with <c>Sfe:Database:MigrateOnStartup</c> off (versions not tracked) they run at every start.
/// </remarks>
public interface IUpgradeStep
{
    /// <summary>The version that introduced the change: installs before it run the step.</summary>
    SemanticVersion Version { get; }

    /// <summary>What the step does, for the update page's log.</summary>
    string Description { get; }

    Task RunAsync(UpgradeContext context, CancellationToken cancellationToken);
}
