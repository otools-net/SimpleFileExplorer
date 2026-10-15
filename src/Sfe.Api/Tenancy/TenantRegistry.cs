using System.Collections.Concurrent;
using Sfe.Api.Maintenance;
using Sfe.Core.Access;

namespace Sfe.Api.Tenancy;

/// <summary>
/// The services of every library (<see cref="TenantHost"/>), started with the application (so that their schedules
/// and trickplay passes run) or when a library is created, stopped when it is disabled or deleted. None starts while
/// the install waits for its update (<see cref="MigrationCoordinator"/>): starting one updates its index.
/// </summary>
public sealed class TenantRegistry(
    IServiceProvider application, IConfiguration configuration, IWebHostEnvironment environment, AccessService accounts, MigrationCoordinator migration,
    ILogger<TenantRegistry> logger)
    : IHostedService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, TenantHost> _hosts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The services of a library; null when it doesn't exist or is disabled.</summary>
    public async Task<TenantHost?> GetAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        if (_hosts.TryGetValue(tenantId, out var host))
            return host;

        var tenant = (await accounts.GetCachedTenantsAsync(cancellationToken)).FirstOrDefault(t => t.Id == tenantId);
        if (tenant is null || tenant.Disabled)
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_hosts.TryGetValue(tenantId, out host))
                return host;
            host = await TenantHost.StartAsync(tenant, application, configuration, environment.ContentRootPath, cancellationToken);
            _hosts[tenantId] = host;
            logger.LogInformation("Library {Library} started ({Root})", tenant.Name, tenant.Root ?? "the configured root");
            return host;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops a library's services (disabled or deleted): its workers stop, and its next request starts them again if it may.</summary>
    public async Task StopAsync(int tenantId)
    {
        if (_hosts.TryRemove(tenantId, out var host))
            await host.DisposeAsync();
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        migration.IsBlocking ? Task.CompletedTask : StartLibrariesAsync(cancellationToken);

    /// <summary>Starts every enabled library (at start-up, or once the install is updated).</summary>
    public async Task StartLibrariesAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await accounts.GetTenantsAsync(cancellationToken))
        {
            if (tenant.Disabled)
                continue;
            try
            {
                await GetAsync(tenant.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The library {Library} cannot start", tenant.Name);
            }
        }
    }

    /// <summary>
    /// Starts every running library again, over the configuration as it is now: the installation page changed the
    /// first library's folder or ffmpeg, which the libraries' services read once.
    /// </summary>
    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        foreach (var id in _hosts.Keys.ToList())
            await StopAsync(id);
        if (!migration.IsBlocking)
            await StartLibrariesAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var id in _hosts.Keys.ToList())
            await StopAsync(id);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _gate.Dispose();
    }
}
