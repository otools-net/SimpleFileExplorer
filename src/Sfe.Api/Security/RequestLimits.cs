using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Sfe.Api.Security;

/// <summary>
/// Limits per caller (the signed-in account, else the network address) on routes that cost the server much more than
/// the request: ffmpeg (thumbnails, probes, streams), reading folders (covers), queries over the whole index (search),
/// large uploads (editor). A browser never meets them (it opens about 6 connections per server and queues the rest);
/// a script flooding them waits its turn instead of taking the server from everyone else (guests included).
/// </summary>
public static class RequestLimits
{
    /// <summary>ffmpeg and folder reads: requests of one caller at once, the next ones queued.</summary>
    public const string Media = "media";

    /// <summary>Searches of the index per minute and caller.</summary>
    public const string Search = "search";

    /// <summary>Uploads of edited pictures (up to 200 MB each, held in memory): one at a time per caller.</summary>
    public const string Upload = "upload";

    /// <summary>Chunks of resumable uploads (16 MB at most, streamed to disk): a few at once per caller, the rest queued.</summary>
    public const string Chunks = "chunks";

    /// <summary>Passwords tried on archives per minute and caller: a guess costs CPU (7z: 2^19 SHA-256 rounds).</summary>
    public const string Unlock = "unlock";

    public const int UnlocksPerMinute = 10;

    public const int MediaConcurrency = 8;
    public const int MediaQueue = 500;
    public const int SearchesPerMinute = 240;

    public static void AddPolicies(RateLimiterOptions options)
    {
        options.AddPolicy(Media, context => RateLimitPartition.GetConcurrencyLimiter(Caller(context), _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = MediaConcurrency,
            QueueLimit = MediaQueue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));
        options.AddPolicy(Search, context => RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = SearchesPerMinute,
            Window = TimeSpan.FromMinutes(1),
        }));
        options.AddPolicy(Unlock, context => RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = UnlocksPerMinute,
            Window = TimeSpan.FromMinutes(1),
        }));
        options.AddPolicy(Chunks, context => RateLimitPartition.GetConcurrencyLimiter(Caller(context), _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 4,
            QueueLimit = 32,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));
        options.AddPolicy(Upload, context => RateLimitPartition.GetConcurrencyLimiter(Caller(context), _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));
    }

    /// <summary>The account when signed in (wherever it connects from), else the address.</summary>
    internal static string Caller(HttpContext context) =>
        context.User.UserId() is { } user ? $"user:{user}" : $"address:{context.Connection.RemoteIpAddress}";
}
