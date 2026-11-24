using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Streaming;
using Otools.Net.Sfe.Core.Thumbnails;

namespace Otools.Net.Sfe.Api.Tenancy;

public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// The types a library's services provide (and the whole server doesn't share): see <see cref="AddSfeTenancy"/>.
    /// </summary>
    public static IReadOnlySet<Type> LibraryTypes { get; private set; } = new HashSet<Type>();

    /// <summary>
    /// The libraries' services (<see cref="TenantRegistry"/>). The routes keep asking for the Core's services (a
    /// <c>LibraryBrowser</c>, the <c>SfeOptions</c>...): <see cref="TenantMiddleware"/> resolves each one in the
    /// request's library. They are registered here too, so that the routes know them as services, but never resolved
    /// here.
    /// </summary>
    public static IServiceCollection AddSfeTenancy(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath
    )
    {
        services.AddSingleton<FfmpegLimits>();
        services.AddSingleton<VideoStreams>();
        services.AddSingleton<Otools.Net.Sfe.Core.Files.Compression.ArchiveBuildGate>();
        services.AddSingleton<Security.ArchiveUnlocks>();
        // The update of an install to this version, which the libraries wait for, and its backups.
        services.AddSingleton<Maintenance.MigrationCoordinator>();
        services.AddSingleton<Maintenance.BackupSources>();
        services.AddSingleton<Maintenance.BackupDownloads>();
        services.AddSingleton<Maintenance.InstallationSettings>();
        services.AddSingleton<Security.InstallationAccess>();
        services.AddSingleton<TenantRegistry>();
        services.AddHostedService(sp => sp.GetRequiredService<TenantRegistry>());
        // The server's log, written while the app runs (after the update gate: AuditLogWorker).
        services.AddHostedService<AuditLogWorker>();
        // The connections' cache: its folder checked, its age limit and removed connections kept.
        services.AddHostedService<ConnectionCacheWorker>();
        // Unfinished uploads into connections: read back at start, the abandoned ones removed.
        services.AddHostedService<RemoteUploadSweepWorker>();
        services.AddScoped<TenantScope>();

        // The types a library's services register, but those the whole server shares (registered here already).
        var library = new ServiceCollection().AddSfeCore(configuration, contentRootPath);
        var forwarded = library
            .Select(d => d.ServiceType)
            .Where(t => t.Assembly == typeof(SfeOptions).Assembly && !t.IsGenericTypeDefinition)
            .Concat([
                typeof(IOptionsMonitor<SfeOptions>),
                typeof(IOptions<SfeOptions>),
                typeof(IDbContextFactory<SfeDbContext>),
            ])
            .Distinct()
            .Where(t => services.All(d => d.ServiceType != t))
            .ToHashSet();
        LibraryTypes = forwarded;
        foreach (var type in forwarded)
        {
            services.AddScoped(type, _ =>
                throw new InvalidOperationException(
                    $"{type.Name} belongs to a library: ask for it in a request of the API (TenantMiddleware)."
                )
            );
        }

        return services;
    }
}
