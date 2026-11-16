using Microsoft.Net.Http.Headers;
using Otools.Net.Sfe.Core.Connections.Previews;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The answers when the connections' cache doesn't take a file (<see cref="ConnectionContentCache"/>).
/// </summary>
public static class ConnectionCacheProblems
{
    /// <summary>Seconds a caller is asked to wait while the cache downloads as many files as it may.</summary>
    private const int BusySeconds = 5;

    public static IResult Of(ConnectionCacheException refused, HttpContext context)
    {
        switch (refused.Reason)
        {
            case CacheRefusal.TooLarge:
                return TypedResults.Problem(
                    "The file is larger than the server downloads to show it: download it instead.",
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    type: ProblemTypes.TooLarge
                );
            case CacheRefusal.Busy:
                context.Response.Headers[HeaderNames.RetryAfter] = BusySeconds.ToString();
                return TypedResults.Problem(
                    "The server already downloads as many files of connections as it may: try again in a moment.",
                    statusCode: StatusCodes.Status429TooManyRequests,
                    type: ProblemTypes.ConnectionCacheBusy
                );
            default:
                // Off, or nothing to read: the file has no preview there.
                return TypedResults.NotFound();
        }
    }
}
