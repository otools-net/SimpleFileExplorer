using Sfe.Core.Trickplay;

namespace Sfe.Api;

/// <summary>Runs the background trickplay generation for the lifetime of the application.</summary>
public sealed class TrickplayWorker(TrickplayService trickplay) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => trickplay.RunAsync(stoppingToken);
}
