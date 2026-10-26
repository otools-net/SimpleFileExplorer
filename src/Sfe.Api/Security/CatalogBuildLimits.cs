using System.Threading.RateLimiting;

namespace Sfe.Api.Security;

/// <summary>
/// Reads of archives' lists started per caller (agents/docs/features/archive-viewer.md): each costs a read of the
/// archive (all of a compressed tar), so a caller opening archives never read before starts at most
/// <see cref="PerMinute"/> a minute. Asked only when a read would start: an archive already read never counts.
/// </summary>
public sealed class CatalogBuildLimits : IDisposable
{
    public const int PerMinute = 30;

    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(caller =>
        RateLimitPartition.GetFixedWindowLimiter(caller, _ => new FixedWindowRateLimiterOptions { PermitLimit = PerMinute, Window = TimeSpan.FromMinutes(1) }));

    /// <summary>For <c>ArchiveCatalogs.Get</c>: may this request's caller start a read now (counted when it says yes).</summary>
    public Func<bool> For(HttpContext context)
    {
        var caller = RequestLimits.Caller(context);
        return () =>
        {
            using var lease = _limiter.AttemptAcquire(caller);
            return lease.IsAcquired;
        };
    }

    public void Dispose() => _limiter.Dispose();
}
