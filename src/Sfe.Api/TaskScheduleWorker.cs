using Sfe.Core.Tasks;

namespace Sfe.Api;

/// <summary>Starts the scheduled background tasks when they come due, for the lifetime of the application.</summary>
public sealed class TaskScheduleWorker(TaskScheduleService schedules) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => schedules.RunAsync(stoppingToken);
}
