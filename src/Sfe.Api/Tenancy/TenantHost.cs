using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Sfe.Core;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Settings;
using Sfe.Core.Streaming;
using Sfe.Core.Thumbnails;

namespace Sfe.Api.Tenancy;

/// <summary>
/// The services of one library (<see cref="ServiceCollectionExtensions.AddSfeCore"/>): its root, index, caches and
/// background workers, over the configuration with the library's own root, connection string and cache folder. What
/// the whole server shares (the server's settings, the ffmpeg limits, the log outputs) comes from the application.
/// </summary>
public sealed class TenantHost : IAsyncDisposable
{
    private readonly List<IHostedService> _workers;

    private TenantHost(TenantInfo tenant, ServiceProvider services, List<IHostedService> workers)
    {
        Tenant = tenant;
        Services = services;
        _workers = workers;
    }

    public TenantInfo Tenant { get; }
    public ServiceProvider Services { get; }

    public static async Task<TenantHost> StartAsync(TenantInfo tenant, IServiceProvider application, IConfiguration configuration, string contentRootPath,
        CancellationToken cancellationToken)
    {
        var tenantConfiguration = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(Overrides(tenant, configuration, contentRootPath))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(tenantConfiguration);
        services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging")));
        // The application's log outputs, as instances: the library's services don't dispose them.
        foreach (var output in application.GetServices<ILoggerProvider>())
            services.AddSingleton(output);
        services.AddSingleton(application.GetRequiredService<SettingsStore>());
        services.AddSingleton(application.GetRequiredService<SettingLayers>());
        services.AddSingleton(application.GetRequiredService<FfmpegLimits>());
        services.AddSingleton(application.GetRequiredService<VideoStreams>());
        services.AddSfeCore(tenantConfiguration, contentRootPath, tenant.Id);
        services.AddSingleton<IHostedService, TrickplayWorker>();
        services.AddSingleton<IHostedService, TaskScheduleWorker>();
        var provider = services.BuildServiceProvider();

        try
        {
            var migrate = configuration.GetSection($"{SfeOptions.SectionName}:{nameof(SfeOptions.Database)}").Get<DatabaseOptions>()?.MigrateOnStartup ?? true;
            if (migrate)
                await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Browsing works without the index: keep serving and let search/scan report the problem.
            provider.GetRequiredService<ILogger<TenantHost>>().LogError(ex,
                "The index of the library {Library} cannot be initialized; search and scan will be unavailable there until it is fixed", tenant.Name);
        }

        var workers = provider.GetServices<IHostedService>().ToList();
        foreach (var worker in workers)
            await worker.StartAsync(cancellationToken);
        return new TenantHost(tenant, provider, workers);
    }

    /// <summary>
    /// What a library changes in the configuration: nothing for the first one (the configuration's own library); the
    /// root, the index's connection string and the cache folder for the others.
    /// </summary>
    internal static Dictionary<string, string?> Overrides(TenantInfo tenant, IConfiguration configuration, string contentRootPath)
    {
        if (tenant.IsDefault)
            return [];

        var section = configuration.GetSection(SfeOptions.SectionName);
        var cache = section.GetSection(nameof(SfeOptions.Thumbnails)).Get<ThumbnailOptions>()?.CacheDirectory ?? new ThumbnailOptions().CacheDirectory;
        return new Dictionary<string, string?>
        {
            [$"{SfeOptions.SectionName}:{nameof(SfeOptions.Root)}"] = tenant.Root,
            [$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] =
                tenant.ConnectionString ?? ConnectionString(tenant.Slug, configuration, contentRootPath),
            [$"{SfeOptions.SectionName}:{nameof(SfeOptions.Thumbnails)}:{nameof(ThumbnailOptions.CacheDirectory)}"] = Path.Combine(cache, "tenants", tenant.Slug),
        };
    }

    /// <summary>A library's thumbnail cache folder (full path), as its services see it, without starting it.</summary>
    public static string CacheDirectory(TenantInfo tenant, IConfiguration configuration, string contentRootPath)
    {
        var key = $"{SfeOptions.SectionName}:{nameof(SfeOptions.Thumbnails)}:{nameof(ThumbnailOptions.CacheDirectory)}";
        var cache = Overrides(tenant, configuration, contentRootPath).GetValueOrDefault(key) ?? configuration[key];
        if (string.IsNullOrWhiteSpace(cache))
            cache = new ThumbnailOptions().CacheDirectory;
        return ThumbnailOptions.ResolveCacheDirectory(cache, contentRootPath);
    }

    /// <summary>
    /// The index of a new library: <c>Sfe:Tenants:ConnectionString</c> with its slug, else next to the first library's
    /// (SQLite: <c>tenants/{slug}.db</c> beside its file; SQL Server: the same server, database <c>{database}_{slug}</c>).
    /// </summary>
    public static string ConnectionString(string slug, IConfiguration configuration, string contentRootPath)
    {
        var section = configuration.GetSection(SfeOptions.SectionName);
        if (section.GetSection(nameof(SfeOptions.Tenants)).Get<TenantOptions>()?.ConnectionString is { Length: > 0 } template)
            return template.Replace("{tenant}", slug, StringComparison.OrdinalIgnoreCase);

        var main = configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ServiceCollectionExtensions.ConnectionStringName}' is not configured.");
        var provider = section.GetSection(nameof(SfeOptions.Database)).Get<DatabaseOptions>()?.Provider ?? DatabaseProvider.SqlServer;
        if (provider == DatabaseProvider.Sqlite)
        {
            var sqlite = new SqliteConnectionStringBuilder(SfeDatabase.PrepareSqlite(main, contentRootPath));
            var folder = Path.GetDirectoryName(sqlite.DataSource) is { Length: > 0 } directory ? directory : contentRootPath;
            sqlite.DataSource = Path.Combine(folder, "tenants", slug + ".db");
            return sqlite.ToString();
        }

        var sql = new SqlConnectionStringBuilder(main);
        sql.InitialCatalog = $"{(string.IsNullOrEmpty(sql.InitialCatalog) ? "SFE" : sql.InitialCatalog)}_{slug}";
        return sql.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var worker in _workers)
        {
            try
            {
                await worker.StopAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await Services.DisposeAsync();
    }
}
