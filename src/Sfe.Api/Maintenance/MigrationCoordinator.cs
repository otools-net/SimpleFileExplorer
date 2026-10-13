using Sfe.Api.Tenancy;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Settings;
using Sfe.Core.Upgrades;
using Sfe.Core.Upgrades.Steps;

namespace Sfe.Api.Maintenance;

public enum MigrationStatus
{
    /// <summary>The install belongs to this version: the app works.</summary>
    None,

    /// <summary>The install belongs to an earlier version: nothing works until its update is confirmed.</summary>
    Required,

    Running,

    /// <summary>The update stopped on an error: it can be started again.</summary>
    Failed,

    /// <summary>The install belongs to a later version: this one must not touch it.</summary>
    Newer,

    /// <summary>The database could not be read at all (e.g. SQL Server not started yet): checked again from time to time.</summary>
    Unavailable,
}

/// <summary>
/// The update of an install to the running version. At start-up the install's version (<see cref="InstalledVersion"/>)
/// is read without changing anything: a new install or one of this version is brought up as before (schema, first
/// space, new rights, settings moved to the libraries); an earlier one stays untouched (no schema change, no library
/// started, every request but the update page's refused by <see cref="Security.RequestGuard"/>) until someone with
/// the setup code confirms the update on that page.
/// </summary>
public sealed class MigrationCoordinator(IServiceProvider services, InstalledVersion installed, IConfiguration configuration, ILogger<MigrationCoordinator> logger)
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _logLock = new();
    private readonly List<string> _log = [];
    private DateTime _nextCheck;

    public static SemanticVersion AppVersion { get; } =
        SemanticVersion.TryParse(Api.AppVersion.Version, out var version) ? version : new SemanticVersion(0, 0, 0);

    /// <summary>Starts as blocking: nothing is served before <see cref="InitializeAsync"/> knew the install's version.</summary>
    public MigrationStatus Status { get; private set; } = MigrationStatus.Unavailable;

    /// <summary>The install's version; null when unknown (before 1.37.0, or not read).</summary>
    public string? DatabaseVersion { get; private set; }

    public string? Error { get; private set; }

    /// <summary>The app cannot be used: only the update page answers.</summary>
    public bool IsBlocking => Status != MigrationStatus.None;

    /// <summary>What the update did so far (the update page shows it while it runs).</summary>
    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_logLock)
                return _log.ToList();
        }
    }

    private bool MigrateOnStartup =>
        configuration.GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.Database)}").Get<DatabaseOptions>()?.MigrateOnStartup ?? true;

    /// <summary>At start-up, before the libraries start (<see cref="TenantRegistry"/> waits for it).</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await CheckAsync(startLibraries: false, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// While the database can't be read, tries again (at most every <see cref="RetryInterval"/>): a server started
    /// before its database server opens once that one answers.
    /// </summary>
    public async Task RetryIfUnavailableAsync(CancellationToken cancellationToken = default)
    {
        if (Status != MigrationStatus.Unavailable || DateTime.UtcNow < _nextCheck || !await _gate.WaitAsync(0, cancellationToken))
            return;
        try
        {
            if (Status == MigrationStatus.Unavailable && DateTime.UtcNow >= _nextCheck)
                await CheckAsync(startLibraries: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Starts the update in the background; false when there is nothing to update (or it runs already).</summary>
    public async Task<bool> StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (Status is not (MigrationStatus.Required or MigrationStatus.Failed))
                return false;
            Status = MigrationStatus.Running;
            Error = null;
            lock (_logLock)
                _log.Clear();
        }
        finally
        {
            _gate.Release();
        }

        _ = Task.Run(RunAsync);
        return true;
    }

    private async Task RunAsync()
    {
        logger.LogWarning("Updating the install from SFE {From} to {To}", DatabaseVersion ?? "(before 1.37.0)", AppVersion);
        try
        {
            await UpdateAsync(strict: true, CancellationToken.None);
            await RunStepsAsync(SemanticVersion.TryParse(DatabaseVersion, out var from) ? from : null, strict: true, CancellationToken.None);
            await StartLibrariesAsync(strict: true, CancellationToken.None);
            await installed.RecordAsync(AppVersion.ToString());
            Note($"The install now belongs to SFE {AppVersion}.");
            logger.LogWarning("Update to SFE {Version} done", AppVersion);
            DatabaseVersion = AppVersion.ToString();
            Status = MigrationStatus.None;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The update to SFE {Version} failed", AppVersion);
            Note("Failed: " + ex.Message);
            Error = ex.Message;
            Status = MigrationStatus.Failed;
        }
    }

    private async Task CheckAsync(bool startLibraries, CancellationToken cancellationToken)
    {
        if (!MigrateOnStartup)
        {
            // Whoever runs the server updates the schema: its version is neither checked nor recorded.
            Status = MigrationStatus.None;
            await UpdateAsync(strict: false, cancellationToken);
            // Nothing tells which steps this install had: all of them, which do nothing when already done.
            await RunStepsAsync(null, strict: false, cancellationToken);
            return;
        }

        InstalledVersionInfo info;
        try
        {
            info = await installed.ReadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (Status != MigrationStatus.Unavailable || Error != ex.Message)
                logger.LogError(ex, "Cannot read the database; SFE stays closed until it can (checked again every {Seconds} s)", RetryInterval.TotalSeconds);
            Status = MigrationStatus.Unavailable;
            Error = ex.Message;
            _nextCheck = DateTime.UtcNow + RetryInterval;
            return;
        }

        Error = null;
        DatabaseVersion = info.Version;
        var known = SemanticVersion.TryParse(info.Version, out var version) ? version : null;
        if (info.Exists && (known is null || known < AppVersion))
        {
            Status = MigrationStatus.Required;
            return;
        }
        if (known is not null && known > AppVersion)
        {
            Status = MigrationStatus.Newer;
            return;
        }

        // A new install, or one of this version: brought up as at every start.
        Status = MigrationStatus.None;
        await UpdateAsync(strict: false, cancellationToken);
        if (known is null)
        {
            try
            {
                await installed.RecordAsync(AppVersion.ToString(), cancellationToken);
                DatabaseVersion = AppVersion.ToString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Cannot record the version of the new install");
            }
        }
        if (startLibraries)
            await StartLibrariesAsync(strict: false, cancellationToken);
    }

    /// <summary>
    /// The schema of the accounts' database, the first space, the rights added since, and the per-library settings
    /// moved out of the settings file. <paramref name="strict"/>: an error stops the update; otherwise it is logged and
    /// the app keeps going (a regular start).
    /// </summary>
    private async Task UpdateAsync(bool strict, CancellationToken cancellationToken)
    {
        if (MigrateOnStartup)
        {
            try
            {
                Note("Updating the accounts' database…");
                // The accounts and libraries first: each library initializes its own index when it starts (TenantHost).
                await services.GetRequiredService<AccessDatabaseInitializer>().InitializeAsync(cancellationToken);
            }
            catch (Exception ex) when (!strict && ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The accounts' database cannot be created or updated; nobody can sign in until it is fixed");
            }
        }

        try
        {
            // The library of the configuration becomes the first library (tenant) with its index, tags and settings.
            var access = services.GetRequiredService<AccessService>();
            var library = await access.EnsureDefaultTenantAsync("Library", cancellationToken);
            // Rights added by this version reach the groups of the spaces created before (once).
            if (await access.GrantNewRightsAsync(cancellationToken) is > 0 and var updated)
            {
                logger.LogInformation("Granted the rights added by this version to the built-in groups of {Count} spaces", updated);
                Note($"Gave the new rights to the built-in groups of {updated} spaces.");
            }
            var layers = services.GetRequiredService<SettingLayers>();
            await layers.MovePerLibrarySettingsAsync(library.Id, cancellationToken);
            layers.NotifyChanged();
        }
        catch (Exception ex) when (!strict && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Cannot read the accounts and libraries; nobody can sign in until the database is fixed");
        }
    }

    /// <summary>
    /// The changes outside the databases' schemas (<see cref="IUpgradeStep"/>) from <paramref name="installed"/> on,
    /// before any library starts: their background jobs would write into what a step moves.
    /// </summary>
    private async Task RunStepsAsync(SemanticVersion? installed, bool strict, CancellationToken cancellationToken)
    {
        var steps = UpgradeSteps.Due(installed, AppVersion);
        if (steps.Count == 0)
            return;

        try
        {
            var contentRoot = services.GetRequiredService<IHostEnvironment>().ContentRootPath;
            var tenants = await services.GetRequiredService<AccessService>().GetTenantsAsync(cancellationToken);
            var context = new UpgradeContext(
                [.. tenants.Select(tenant => TenantHost.CacheDirectory(tenant, configuration, contentRoot))],
                Note,
                logger);
            foreach (var step in steps)
            {
                Note($"{step.Version}: {step.Description}…");
                logger.LogInformation("Upgrade step {Version}: {Description}", step.Version, step.Description);
                await step.RunAsync(context, cancellationToken);
            }
        }
        catch (Exception ex) when (!strict && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "An upgrade step failed");
        }
    }

    /// <summary>Starts every library, which updates its index database (strict: an index that can't be updated fails the update).</summary>
    private async Task StartLibrariesAsync(bool strict, CancellationToken cancellationToken)
    {
        var registry = services.GetRequiredService<TenantRegistry>();
        if (!strict)
        {
            await registry.StartLibrariesAsync(cancellationToken);
            return;
        }

        foreach (var tenant in await services.GetRequiredService<AccessService>().GetTenantsAsync(cancellationToken))
        {
            if (tenant.Disabled)
                continue;
            Note($"Updating the index of {tenant.Name}…");
            var host = await registry.GetAsync(tenant.Id, cancellationToken)
                ?? throw new InvalidOperationException($"The library {tenant.Name} cannot start.");
            // Done once by the start already: again, so that its error (logged there) stops the update here.
            if (MigrateOnStartup)
                await host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
        }
    }

    private void Note(string line)
    {
        lock (_logLock)
            _log.Add(line);
    }
}
