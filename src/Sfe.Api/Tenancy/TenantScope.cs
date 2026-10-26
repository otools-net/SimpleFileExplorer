using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Streaming;
using Sfe.Core.Thumbnails;

namespace Sfe.Api.Tenancy;

/// <summary>The services of the request's library, found once its session is known (<see cref="TenantMiddleware"/>).</summary>
public sealed class TenantScope(TenantRegistry registry, CurrentAccess current)
{
    private TenantHost? _host;

    public TenantHost Host => _host ?? throw new InvalidOperationException("The request's library is not known (it is unavailable, or the request is not an API one).");

    public async Task<TenantHost?> InitializeAsync(CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        _host = state.Tenant is null ? null : await registry.GetAsync(state.TenantId, cancellationToken);
        return _host;
    }
}

/// <summary>
/// Finds the services of the request's library. A client that names the library it shows (<see cref="Header"/>) gets
/// 409 <c>tenant-changed</c> when the session is in another one by now (switched in another tab): it reloads.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public const string Header = "X-SFE-Tenant";
    public const string TenantChangedType = "tenant-changed";

    public async Task InvokeAsync(HttpContext context, TenantScope scope, CurrentAccess current)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/openapi")
            || path.StartsWithSegments("/api/setup") || path.StartsWithSegments("/api/session") || path.StartsWithSegments("/api/docs")
            || path.StartsWithSegments("/api/migration") || path.StartsWithSegments("/api/installation"))
        {
            await next(context);
            return;
        }

        var state = await current.GetAsync();
        if (context.Request.Headers.TryGetValue(Header, out var shown) && int.TryParse(shown, out var shownId) && shownId != state.TenantId)
        {
            await TypedResults.Problem("The session is in another space now: reload the page.", statusCode: StatusCodes.Status409Conflict, type: TenantChangedType)
                .ExecuteAsync(context);
            return;
        }

        if (await scope.InitializeAsync(context.RequestAborted) is not { } host)
        {
            await TypedResults.Problem("This space is not available.", statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
            return;
        }

        // The routes' Core services come from the library's own container; its singletons are not the request's to dispose.
        var original = context.RequestServices;
        context.RequestServices = new TenantRequestServices(original, host.Services);
        try
        {
            await next(context);
        }
        finally
        {
            context.RequestServices = original;
        }
    }
}

/// <summary>
/// The request's services, with the Core's own (<see cref="TenancyServiceCollectionExtensions.LibraryTypes"/>) taken
/// from the request's library.
/// </summary>
internal sealed class TenantRequestServices(IServiceProvider request, IServiceProvider library) : IKeyedServiceProvider, ISupportRequiredService
{
    public object? GetService(Type serviceType) =>
        TenancyServiceCollectionExtensions.LibraryTypes.Contains(serviceType) ? library.GetService(serviceType) : request.GetService(serviceType);

    public object GetRequiredService(Type serviceType) =>
        GetService(serviceType) ?? throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");

    // Keyed services (OpenAPI's documents...) are never a library's.
    public object? GetKeyedService(Type serviceType, object? serviceKey) => ((IKeyedServiceProvider)request).GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        ((IKeyedServiceProvider)request).GetRequiredKeyedService(serviceType, serviceKey);
}

public static class TenancyServiceCollectionExtensions
{
    /// <summary>The types a library's services provide (and the whole server doesn't share): see <see cref="AddSfeTenancy"/>.</summary>
    public static IReadOnlySet<Type> LibraryTypes { get; private set; } = new HashSet<Type>();

    /// <summary>
    /// The libraries' services (<see cref="TenantRegistry"/>). The routes keep asking for the Core's services (a
    /// <c>LibraryBrowser</c>, the <c>SfeOptions</c>...): <see cref="TenantMiddleware"/> resolves each one in the request's
    /// library. They are registered here too, so that the routes know them as services, but never resolved here.
    /// </summary>
    public static IServiceCollection AddSfeTenancy(this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        services.AddSingleton<FfmpegLimits>();
        services.AddSingleton<VideoStreams>();
        services.AddSingleton<Sfe.Core.Files.Compression.ArchiveBuildGate>();
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
        services.AddScoped<TenantScope>();

        // The types a library's services register, but those the whole server shares (registered here already).
        var library = new ServiceCollection().AddSfeCore(configuration, contentRootPath);
        var forwarded = library
            .Select(d => d.ServiceType)
            .Where(t => t.Assembly == typeof(SfeOptions).Assembly && !t.IsGenericTypeDefinition)
            .Concat([typeof(IOptionsMonitor<SfeOptions>), typeof(IOptions<SfeOptions>), typeof(IDbContextFactory<SfeDbContext>)])
            .Distinct()
            .Where(t => services.All(d => d.ServiceType != t))
            .ToHashSet();
        LibraryTypes = forwarded;
        foreach (var type in forwarded)
            services.AddScoped(type, _ => throw new InvalidOperationException($"{type.Name} belongs to a library: ask for it in a request of the API (TenantMiddleware)."));
        return services;
    }
}
