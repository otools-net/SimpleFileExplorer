using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Backup;
using Sfe.Core.Data;
using Sfe.Core.Editing;
using Sfe.Core.FileTracking;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Search;
using Sfe.Core.Settings;
using Sfe.Core.SystemCommands;
using Sfe.Core.Tagging;
using Sfe.Core.Tasks;
using Sfe.Core.Streaming;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;
using Sfe.Core.Upgrades;

namespace Sfe.Core;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "Sfe";

    /// <param name="contentRootPath">
    /// Folder that relative paths in the settings (thumbnail cache, SQLite database, settings file) are resolved
    /// against; the current directory when null.
    /// </param>
    /// <param name="tenantId">The library these services work for; null = the first library (the configuration's).</param>
    public static IServiceCollection AddSfeCore(this IServiceCollection services, IConfiguration configuration, string? contentRootPath = null,
        int? tenantId = null)
    {
        var basePath = contentRootPath ?? Directory.GetCurrentDirectory();
        var configured = configuration.GetSection(SfeOptions.SectionName).Get<SfeOptions>() ?? new SfeOptions();

        // Settings saved from the web client: applied over the configuration, and a save updates IOptionsMonitor.
        // With the accounts' database (AddSfeAccess), by level: the server's, then the library's (SettingLayers).
        AddSettingsStore(services, configured, basePath);
        services.AddSingleton<IOptionsChangeTokenSource<SfeOptions>>(sp => sp.GetRequiredService<SettingsStore>());
        services.AddSingleton<IOptionsChangeTokenSource<SfeOptions>>(sp =>
            (IOptionsChangeTokenSource<SfeOptions>?)sp.GetService<SettingLayers>() ?? new ConfigurationChangeTokenSource<SfeOptions>(configuration));
        services.AddSingleton(new TenantBinding(tenantId));

        services.AddOptions<SfeOptions>()
            .Bind(configuration.GetSection(SfeOptions.SectionName))
            .PostConfigure(options => options.FileTypes.ApplyDefaults(list =>
                configuration.GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.FileTypes)}:{list}").Exists()))
            .PostConfigure<IServiceProvider>((options, sp) =>
            {
                if (sp.GetService<SettingLayers>() is { } layers)
                    layers.ApplyTo(options, sp.GetRequiredService<TenantBinding>().TenantId);
                else
                    sp.GetRequiredService<SettingsStore>().Apply(options);
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.Root), $"{SfeOptions.SectionName}:{nameof(SfeOptions.Root)} is required.")
            .PostConfigure(options =>
            {
                var cache = options.Thumbnails.CacheDirectory;
                if (!string.IsNullOrWhiteSpace(cache))
                    options.Thumbnails.CacheDirectory = ThumbnailOptions.ResolveCacheDirectory(cache, basePath);
            })
            .ValidateOnStart();

        var provider = configured.Database.Provider;
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        if (provider == DatabaseProvider.Sqlite)
            connectionString = SfeDatabase.PrepareSqlite(connectionString, contentRootPath);
        services.AddDbContextFactory<SfeDbContext>(options => SfeDatabase.Configure(options, provider, connectionString));

        services.AddSingleton<LibraryPaths>();
        services.AddSingleton<MediaClassifier>();
        services.AddSingleton<LibraryBrowser>();
        services.AddSingleton<FileSearchService>();
        services.AddSingleton<MediaDimensions>();
        services.AddSingleton<DirectoryIndexer>();
        services.AddSingleton<ScanCoordinator>();
        services.AddSingleton<Ffmpeg>();
        // Shared by every library's services when the server hosts several (the host registers its own first).
        services.TryAddSingleton<FfmpegLimits>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<TrickplayService>();
        // Shared by every library (the application's instance, handed to each one), like FfmpegLimits.
        services.TryAddSingleton<VideoStreams>();
        services.AddSingleton<VideoStreamer>();
        services.AddSingleton<SystemCommandService>();
        services.AddSingleton<TagService>();
        services.AddSingleton<ScopeTagger>();
        services.AddSingleton<EditedImages>();
        services.AddSingleton<DuplicateFinder>();
        services.AddSingleton<DuplicateRemover>();
        services.AddSingleton<SimilarVideoFinder>();
        services.AddSingleton<RelinkService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<TaskHistory>();
        services.AddSingleton<BackgroundTasks>();
        services.AddSingleton<TaskScheduleService>();

        // The logs of the task runs: SFE's own entries from Debug up, whatever the configured levels (which apply to
        // the console and the other providers).
        services.AddSingleton<ILoggerProvider, TaskLogProvider>();
        services.Configure<LoggerFilterOptions>(options => options.AddFilter<TaskLogProvider>("Sfe", LogLevel.Debug));

        return services;
    }

    /// <summary>
    /// Accounts, groups, rights and libraries: one set for the whole server, in the database of
    /// <c>ConnectionStrings:Sfe</c> (<see cref="AccessDbContext"/>).
    /// </summary>
    public static IServiceCollection AddSfeAccess(this IServiceCollection services, IConfiguration configuration, string? contentRootPath = null)
    {
        var section = configuration.GetSection(SfeOptions.SectionName);
        services.AddOptions<SetupOptions>().Bind(section.GetSection(nameof(SfeOptions.Setup)));
        services.AddOptions<SecurityOptions>().Bind(section.GetSection(nameof(SfeOptions.Security)));
        services.AddOptions<RightOptions>().Bind(section.GetSection(nameof(SfeOptions.Rights)));
        services.AddOptions<TenantOptions>().Bind(section.GetSection(nameof(SfeOptions.Tenants)));
        services.AddOptions<BackupOptions>().Bind(section.GetSection(nameof(SfeOptions.Backup)));

        var provider = section.GetSection(nameof(SfeOptions.Database)).Get<DatabaseOptions>()?.Provider ?? DatabaseProvider.SqlServer;
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        if (provider == DatabaseProvider.Sqlite)
            connectionString = SfeDatabase.PrepareSqlite(connectionString, contentRootPath);
        services.AddDbContextFactory<AccessDbContext>(options =>
            SfeDatabase.Configure(options, provider, connectionString, AccessDbContext.MigrationsHistoryTable));

        services.AddSingleton<AccessChanges>();
        services.AddSingleton<AccessDatabaseInitializer>();
        services.AddSingleton<AccessService>();
        services.AddSingleton<RightsEvaluator>();
        services.AddSingleton<IUserDirectory, LocalUserDirectory>();
        services.AddSingleton<SignInService>();
        services.AddSingleton<SetupService>();
        // The version the install belongs to (read before anything else at start-up), and its backups.
        services.AddSingleton<InstalledVersion>();
        services.AddSingleton<BackupWriter>();

        // Settings by level: the server's settings file, the libraries' and users' rows.
        AddSettingsStore(services, section.Get<SfeOptions>() ?? new SfeOptions(), contentRootPath ?? Directory.GetCurrentDirectory());
        services.AddSingleton<SettingRows>();
        services.AddSingleton<SettingLayers>();
        return services;
    }

    private static void AddSettingsStore(IServiceCollection services, SfeOptions configured, string basePath)
    {
        var settingsFile = Path.GetFullPath(LibraryPaths.ExpandHome(configured.SettingsFile), basePath);
        services.TryAddSingleton(sp => new SettingsStore(settingsFile, sp.GetRequiredService<ILogger<SettingsStore>>()));
    }
}
