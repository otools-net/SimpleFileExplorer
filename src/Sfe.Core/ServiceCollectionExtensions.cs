using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Editing;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.FileTracking;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.KeepAlive;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Logs;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Search;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Core.Streaming;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Core.Tasks;
using Otools.Net.Sfe.Core.Thumbnails;
using Otools.Net.Sfe.Core.Trickplay;

namespace Otools.Net.Sfe.Core;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "Sfe";

    /// <param name="contentRootPath">
    /// Folder that relative paths in the settings (thumbnail cache, SQLite database, settings file) are resolved
    /// against; the current directory when null.
    /// </param>
    /// <param name="tenantId">The library these services work for; null = the first library (the
    /// configuration's).</param>
    public static IServiceCollection AddSfeCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null,
        int? tenantId = null
    )
    {
        var basePath = contentRootPath ?? Directory.GetCurrentDirectory();
        var configured = configuration.GetSection(SfeOptions.SectionName).Get<SfeOptions>() ?? new SfeOptions();

        // Settings saved from the web client: applied over the configuration, and a save updates IOptionsMonitor.
        // With the accounts' database (AddSfeAccess), by level: the server's, then the library's (SettingLayers).
        AddSettingsStore(services, configured, basePath);
        services.AddSingleton<IOptionsChangeTokenSource<SfeOptions>>(sp => sp.GetRequiredService<SettingsStore>());
        services.AddSingleton<IOptionsChangeTokenSource<SfeOptions>>(sp =>
            (IOptionsChangeTokenSource<SfeOptions>?)sp.GetService<SettingLayers>()
            ?? new ConfigurationChangeTokenSource<SfeOptions>(configuration)
        );
        services.AddSingleton(new TenantBinding(tenantId));
        AddLibraryOptions(services, configuration, basePath);

        var provider = configured.Database.Provider;
        var connectionString = ConnectionStringOf(configuration, provider, contentRootPath);
        services.AddDbContextFactory<SfeDbContext>(options =>
            SfeDatabase.Configure(options, provider, connectionString)
        );

        AddIndexing(services);
        AddMedia(services);
        AddFiles(services);
        AddTasks(services);

        // The logs of the task runs: SFE's own entries from Debug up, whatever the configured levels (which apply to
        // the console and the other providers).
        services.AddSingleton<ILoggerProvider, TaskLogProvider>();
        services.Configure<LoggerFilterOptions>(options => options.AddFilter<TaskLogProvider>("Sfe", LogLevel.Debug));
        AddLegacyLogCategories(services);

        return services;
    }

    /// <summary>
    /// The library's <see cref="SfeOptions"/>: the configuration, its file type defaults, the saved settings over it
    /// (by level when the accounts' database is there), and the thumbnail cache resolved against
    /// <paramref name="basePath"/>.
    /// </summary>
    private static void AddLibraryOptions(IServiceCollection services, IConfiguration configuration, string basePath) =>
        services
            .AddOptions<SfeOptions>()
            .Bind(configuration.GetSection(SfeOptions.SectionName))
            .PostConfigure(options =>
                options.FileTypes.ApplyDefaults(list =>
                    configuration.GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.FileTypes)}:{list}").Exists()
                )
            )
            .PostConfigure<IServiceProvider>(
                (options, sp) =>
                {
                    if (sp.GetService<SettingLayers>() is { } layers)
                        layers.ApplyTo(options, sp.GetRequiredService<TenantBinding>().TenantId);
                    else
                        sp.GetRequiredService<SettingsStore>().Apply(options);
                }
            )
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Root),
                $"{SfeOptions.SectionName}:{nameof(SfeOptions.Root)} is required."
            )
            .PostConfigure(options =>
            {
                var cache = options.Thumbnails.CacheDirectory;
                if (!string.IsNullOrWhiteSpace(cache))
                    options.Thumbnails.CacheDirectory = ThumbnailOptions.ResolveCacheDirectory(cache, basePath);
            })
            .ValidateOnStart();

    /// <summary>
    /// The configured connection string, its SQLite file prepared (made absolute against
    /// <paramref name="contentRootPath"/>).
    /// </summary>
    internal static string ConnectionStringOf(
        IConfiguration configuration,
        DatabaseProvider provider,
        string? contentRootPath
    )
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        return provider == DatabaseProvider.Sqlite
            ? SfeDatabase.PrepareSqlite(connectionString, contentRootPath)
            : connectionString;
    }

    /// <summary>The library's paths, listings, search and scans.</summary>
    private static void AddIndexing(IServiceCollection services)
    {
        services.AddSingleton<LibraryPaths>();
        services.AddSingleton<MediaClassifier>();
        services.AddSingleton<LibraryBrowser>();
        services.AddSingleton<Library.Watching.FolderWatchers>();
        services.AddSingleton<SearchTermSettings>();
        services.AddSingleton<FileSearchService>();
        services.AddSingleton<MediaDimensions>();
        services.AddSingleton<FolderDefaultTags>();
        services.AddSingleton<DirectoryIndexer>();
        services.AddSingleton<ScanCoordinator>();
        services.AddSingleton<ListingIndexer>();
    }

    /// <summary>
    /// ffmpeg and what it makes: thumbnails, trickplay, video streams; the network folders kept awake; the tags.
    /// </summary>
    private static void AddMedia(IServiceCollection services)
    {
        services.AddSingleton<Ffmpeg>();
        // Shared by every library's services when the server hosts several (the host registers its own first).
        services.TryAddSingleton<FfmpegLimits>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<TrickplayService>();
        // Shared by every library (the application's instance, handed to each one), like FfmpegLimits.
        services.TryAddSingleton<VideoStreams>();
        services.AddSingleton<VideoStreamer>();
        services.AddSingleton<KeepAliveService>();
        services.AddSingleton<TagService>();
        services.AddSingleton<ScopeTagger>();
    }

    /// <summary>
    /// What changes the library's files: edits, the trash, move and copy jobs, transfers to connections, uploads,
    /// archives, properties.
    /// </summary>
    private static void AddFiles(IServiceCollection services)
    {
        services.AddSingleton<EditedImages>();
        services.AddSingleton<WorkFolder>();
        services.AddSingleton<PathMoves>();
        services.AddSingleton<Trash>();
        services.AddSingleton<JobBoard>();
        services.AddSingleton<ItemsBeingWritten>();
        services.AddSingleton<LibraryClashes>();
        services.AddSingleton<LibraryCopies>();
        services.AddSingleton<FileJobs>();
        services.AddSingleton<Transfers.LibraryTransfers>();
        services.AddSingleton<Transfers.LibraryTransferTags>();
        services.AddSingleton<Transfers.TransferTagSides>();
        services.AddSingleton<Transfers.TransferJobs>();
        AddConnectionIndex(services);
        services.AddSingleton<Uploads>();
        services.AddSingleton<Archives>();
        // Shared by every library (the application's instance, handed to each one), like FfmpegLimits.
        services.TryAddSingleton<Files.Compression.ArchiveBuildGate>();
        services.AddSingleton<Files.Compression.ArchiveCatalogs>();
        services.AddSingleton<Properties>();
        services.AddSingleton<FileContent>();
        services.AddSingleton<PropertyJobs>();
        services.AddSingleton<FileOperations>();
    }

    /// <summary>The background tasks, what they run (duplicates, similar videos, relinking), their schedules.</summary>
    private static void AddTasks(IServiceCollection services)
    {
        services.AddSingleton<DuplicateFinder>();
        services.AddSingleton<DuplicateRemover>();
        services.AddSingleton<SimilarVideoFinder>();
        services.AddSingleton<RelinkService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<TaskHistory>();
        services.AddSingleton<BackgroundTasks>();
        services.AddSingleton<TaskScheduleService>();
    }

    /// <summary>
    /// The library's index of connections' files (agents/docs/features/connection-index.md); the server's
    /// part (<see cref="Connections.Indexing.ConnectionIndexSources"/>) is handed to each library by its host.
    /// </summary>
    private static void AddConnectionIndex(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Connections.Indexing.ConnectionIndexLocks>();
        services.AddSingleton<Connections.Indexing.RemoteIndexTransactions>();
        services.AddSingleton<Connections.Indexing.RemoteIndexCleanup>();
        services.AddSingleton<Connections.Indexing.ConnectionIndexer>();
        services.AddSingleton<Connections.Indexing.ConnectionIndexes>();
        services.AddSingleton<Connections.Indexing.ConnectionWatchers>();
        services.AddSingleton<Connections.Indexing.ConnectionIndexUpdates>();
        services.AddSingleton<Connections.Indexing.IndexListingPlaces>();
        services.AddSingleton<Connections.Indexing.RemoteIndexListings>();
        services.AddSingleton<Connections.Indexing.RemoteIndexSearch>();
        services.AddSingleton<Connections.Indexing.RemoteFileTags>();
        services.AddSingleton<Connections.Indexing.RemoteScopeTagger>();
        // Previews of connections' files; their cache is the server's (AddConnectionCache), handed to each library.
        services.AddSingleton<Connections.Indexing.RemoteMediaRows>();
        services.AddSingleton<Connections.Previews.ConnectionPreviews>();
    }

    /// <summary>
    /// The log levels set for the categories named <c>Sfe…</c> before the namespaces became <c>Otools.Net.Sfe.*</c>
    /// still apply (<see cref="LegacyLogCategories"/>): in the server's container and in each library's.
    /// </summary>
    internal static void AddLegacyLogCategories(IServiceCollection services) =>
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<LoggerFilterOptions>, LegacyLogCategories>()
        );

    internal static void AddSettingsStore(IServiceCollection services, SfeOptions configured, string basePath)
    {
        var settingsFile = Path.GetFullPath(LibraryPaths.ExpandHome(configured.SettingsFile), basePath);
        services.TryAddSingleton(sp => new SettingsStore(
            settingsFile,
            sp.GetRequiredService<ILogger<SettingsStore>>()
        ));
    }
}
