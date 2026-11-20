using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Backup;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Dropbox;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Logs;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Core.Upgrades;

namespace Otools.Net.Sfe.Core;

/// <summary>
/// The services of the whole server (<see cref="AddSfeAccess"/>): accounts, rights, libraries, connections,
/// settings by level and the server's log. Each library's own services are
/// <see cref="ServiceCollectionExtensions.AddSfeCore"/>.
/// </summary>
public static class AccessServiceCollectionExtensions
{
    /// <summary>
    /// Accounts, groups, rights and libraries: one set for the whole server, in the database of
    /// <c>ConnectionStrings:Sfe</c> (<see cref="AccessDbContext"/>).
    /// </summary>
    public static IServiceCollection AddSfeAccess(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null
    )
    {
        ServiceCollectionExtensions.AddLegacyLogCategories(services);
        var section = configuration.GetSection(SfeOptions.SectionName);
        var root = contentRootPath ?? Directory.GetCurrentDirectory();
        var codePath = AddSetupCode(services, section, root);
        services.AddOptions<SecurityOptions>().Bind(section.GetSection(nameof(SfeOptions.Security)));
        services.AddOptions<RightOptions>().Bind(section.GetSection(nameof(SfeOptions.Rights)));
        services.AddOptions<TenantOptions>().Bind(section.GetSection(nameof(SfeOptions.Tenants)));
        services.AddOptions<BackupOptions>().Bind(section.GetSection(nameof(SfeOptions.Backup)));

        var provider = section.GetSection(nameof(SfeOptions.Database)).Get<DatabaseOptions>()?.Provider
            ?? DatabaseProvider.SqlServer;
        var connectionString = ServiceCollectionExtensions.ConnectionStringOf(configuration, provider, contentRootPath);
        services.AddDbContextFactory<AccessDbContext>(options =>
            SfeDatabase.Configure(options, provider, connectionString, AccessDbContext.MigrationsHistoryTable)
        );

        AddAccounts(services, codePath);
        AddConnections(services, section, codePath, root);
        // The version the install belongs to (read before anything else at start-up), and its backups.
        services.AddSingleton<InstalledVersion>();
        services.AddSingleton<BackupWriter>();

        // Settings by level: the server's settings file, the libraries' and users' rows.
        ServiceCollectionExtensions.AddSettingsStore(services, section.Get<SfeOptions>() ?? new SfeOptions(), root);
        services.AddSingleton<SettingRows>();
        services.AddSingleton<SettingLayers>();

        AddServerLog(services, section, root);
        return services;
    }

    /// <summary>
    /// The setup code's options and, without a configured code, the one the server made itself (written at start-up
    /// when needed).
    /// </summary>
    /// <returns>The setup code file's full path: the recovery codes and the providers' secrets go beside it.</returns>
    private static string AddSetupCode(IServiceCollection services, IConfigurationSection section, string root)
    {
        services.AddOptions<SetupOptions>().Bind(section.GetSection(nameof(SfeOptions.Setup)));
        var codeFile = section.GetSection(nameof(SfeOptions.Setup)).Get<SetupOptions>()?.CodeFile
            is { Length: > 0 } file
            ? file
            : new SetupOptions().CodeFile;
        var codePath = Path.GetFullPath(LibraryPaths.ExpandHome(codeFile), root);
        services.AddSingleton(sp => new GeneratedSetupCode(
            codePath,
            sp.GetRequiredService<ILogger<GeneratedSetupCode>>()
        ));
        services.AddSingleton<IPostConfigureOptions<SetupOptions>>(sp => sp.GetRequiredService<GeneratedSetupCode>());
        services.AddSingleton<IOptionsChangeTokenSource<SetupOptions>>(sp =>
            sp.GetRequiredService<GeneratedSetupCode>()
        );
        return codePath;
    }

    /// <summary>Accounts, groups, rights, sign-in, setup, recovery, quick links, folder rules, access tokens.</summary>
    private static void AddAccounts(IServiceCollection services, string codePath)
    {
        services.AddSingleton<AccessChanges>();
        services.AddSingleton<AccessDatabaseInitializer>();
        services.AddSingleton<AccessService>();
        services.AddSingleton<RightsEvaluator>();
        services.AddSingleton<IUserDirectory, LocalUserDirectory>();
        services.AddSingleton<SignInService>();
        services.AddSingleton<SetupService>();
        // Its codes are kept beside the setup code (a service has no console to show them in).
        services.AddSingleton(sp => new AccountRecovery(
            sp.GetRequiredService<IDbContextFactory<AccessDbContext>>(),
            sp.GetRequiredService<AccessService>(),
            Path.Combine(Path.GetDirectoryName(codePath)!, "recovery-code.txt"),
            sp.GetRequiredService<ILogger<AccountRecovery>>()
        ));
        services.AddSingleton<QuickLinkService>();
        services.AddSingleton<FolderRuleService>();
        services.AddSingleton<AccessTokens>();
    }

    /// <summary>
    /// Connections to other storage, for the whole server: providers, credentials, sign-ins, listings, uploads, the
    /// changes told to every library, and the connections' cache.
    /// </summary>
    private static void AddConnections(
        IServiceCollection services,
        IConfigurationSection section,
        string codePath,
        string root
    )
    {
        // Connections to other storage: the kinds registered (IConnectionProvider) and the door their calls leave by.
        services.TryAddSingleton(_ => new OutboundPolicy());
        services.AddSingleton<ConnectionProviders>();
        // The secrets of the providers' apps typed on the settings page, beside the setup code.
        services.AddSingleton(new ProviderSecrets(Path.Combine(Path.GetDirectoryName(codePath)!, "connections")));
        services.AddSingleton<IConnectionProvider, DropboxProvider>();
        // Their credentials are protected by the Api's keys (ICredentialProtector, AddSfeSecurity).
        services.AddSingleton<ConnectionService>();
        services.AddSingleton<ConnectionSharing>();
        services.AddSingleton<ConnectionRuleService>();
        services.AddSingleton<ConnectionAccess>();
        services.AddSingleton<ConnectionPolicies>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PendingSignIns>();
        services.AddSingleton<ConnectionSignInFlow>();
        services.AddSingleton<RemoteListings>();
        services.AddSingleton<ConnectionGates>();
        services.AddSingleton<Transfers.TransferCalls>();
        services.AddSingleton<RemoteUploads>();
        // What SFE changes in connections, told to every library's index of them and to the change feeds.
        services.AddSingleton<ConnectionChanges>();
        services.AddSingleton<Connections.Indexing.ConnectionIndexSources>();
        AddConnectionCache(services, section, root);
    }

    /// <summary>
    /// The server's log: every logger's entries (also the libraries', TenantHost), in the database and daily files.
    /// </summary>
    private static void AddServerLog(IServiceCollection services, IConfigurationSection section, string root)
    {
        var logsDirectory = section.GetSection(nameof(SfeOptions.Logs)).Get<LogOptions>()?.Directory
            is { Length: > 0 } logs
            ? logs
            : new LogOptions().Directory;
        logsDirectory = Path.GetFullPath(LibraryPaths.ExpandHome(logsDirectory), root);
        services.AddSingleton<AuditLogProvider>();
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<AuditLogProvider>());
        services.AddSingleton(sp => new AuditLogService(
            sp.GetRequiredService<AuditLogProvider>(),
            sp.GetRequiredService<IDbContextFactory<AccessDbContext>>(),
            sp.GetRequiredService<SettingLayers>(),
            logsDirectory,
            sp.GetRequiredService<ILogger<AuditLogService>>()
        ));
    }

    /// <summary>
    /// The connections' cache (agents/docs/features/connection-previews.md): one for the server, in
    /// <c>{Thumbnails:CacheDirectory}/connections</c> of the configuration (not a library's own cache folder).
    /// </summary>
    private static void AddConnectionCache(IServiceCollection services, IConfigurationSection section, string root)
    {
        var cache = section.GetSection(nameof(SfeOptions.Thumbnails)).Get<ThumbnailOptions>()?.CacheDirectory
            is { Length: > 0 } configured
            ? configured
            : new ThumbnailOptions().CacheDirectory;
        var folder = Path.Combine(ThumbnailOptions.ResolveCacheDirectory(cache, root), "connections");
        services.AddSingleton(sp => new Connections.Previews.ConnectionCacheStore(
            folder,
            sp.GetRequiredService<SettingLayers>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<Connections.Previews.ConnectionCacheStore>>()
        ));
        services.AddSingleton<Connections.Previews.ConnectionThumbnailStore>();
        services.AddSingleton<Connections.Previews.ConnectionContentCache>();
        services.AddSingleton<Connections.Previews.RemoteThumbnailBatches>();
    }
}
