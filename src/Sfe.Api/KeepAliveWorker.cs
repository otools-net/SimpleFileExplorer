using Sfe.Core.KeepAlive;

namespace Sfe.Api;

/// <summary>Keeps the library's folder alive on its file server for the lifetime of the application (one per library).</summary>
public sealed class KeepAliveWorker(KeepAliveService keepAlive) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => keepAlive.RunAsync(stoppingToken);
}
