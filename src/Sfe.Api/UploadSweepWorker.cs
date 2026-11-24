using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api;

/// <summary>
/// Removes the library's abandoned uploads (<see cref="Uploads.RemoveAbandoned"/>) at start and every
/// <see cref="Interval"/>, so that their parts don't fill the disk while nobody starts or lists uploads (one per
/// library).
/// </summary>
public sealed class UploadSweepWorker(Uploads uploads, ILogger<UploadSweepWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    uploads.RemoveAbandoned();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not remove the abandoned uploads");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
