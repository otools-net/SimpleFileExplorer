using Microsoft.Net.Http.Headers;
using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The answers of a connection's file routes when the connection or its provider fails: a problem whose
/// <c>type</c> word says what to do (sign in again, wait, try later). A provider's refused sign-in marks the
/// connection, so nobody opens it until someone signs in again.
/// </summary>
public static class ConnectionProblems
{
    /// <summary>409: the provider no longer accepts the sign-in; the connection's manager signs in again.</summary>
    public const string NeedsReconnectingType = ProblemTypes.ConnectionNeedsReconnecting;

    /// <summary>409: the connection is off, or its kind or level is no longer allowed.</summary>
    public const string UnusableType = ProblemTypes.ConnectionUnusable;

    /// <summary>429 with <c>Retry-After</c>: the provider asks to wait.</summary>
    public const string RateLimitedType = ProblemTypes.ConnectionRateLimited;

    /// <summary>502: the provider cannot be reached, or failed on its side; trying later may work.</summary>
    public const string UnavailableType = ProblemTypes.ConnectionUnavailable;

    /// <summary>403: the provider does not let this account do it.</summary>
    public const string RefusedType = ProblemTypes.ConnectionRefused;

    /// <summary>409: the provider lacks the function.</summary>
    public const string NotSupportedType = ProblemTypes.ConnectionNotSupported;

    /// <summary>429 while the provider's wait lasts, or while the connection's calls are all taken.</summary>
    public static IResult Waiting(TimeSpan left, HttpContext context)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(left.TotalSeconds));
        context.Response.Headers[HeaderNames.RetryAfter] = seconds.ToString();
        return TypedResults.Problem(
            $"The provider asks to wait: try again in {seconds} s.",
            statusCode: StatusCodes.Status429TooManyRequests,
            type: RateLimitedType
        );
    }

    /// <summary>The answer to <see cref="ConnectionService.OpenAsync"/>'s conflict.</summary>
    public static IResult OfOpening(string? problem) =>
        TypedResults.Problem(
            problem,
            statusCode: StatusCodes.Status409Conflict,
            type: problem == ConnectionService.NeedsReconnectingProblem ? NeedsReconnectingType : UnusableType
        );

    /// <summary>The answer to a provider's error; marks the connection when its sign-in ended.</summary>
    public static async Task<IResult> OfAsync(
        RemoteStorageException error,
        int connectionId,
        HttpContext context,
        ConnectionService connections
    )
    {
        if (error.Kind == RemoteErrorKind.CredentialsEnded)
        {
            await connections.MarkNeedsReconnectingAsync(connectionId, CancellationToken.None);
            return OfOpening(ConnectionService.NeedsReconnectingProblem);
        }
        if (error is { Kind: RemoteErrorKind.RateLimited, RetryAfter: { } wait })
            context.Response.Headers[HeaderNames.RetryAfter] = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();

        return error.Kind switch
        {
            RemoteErrorKind.NotFound => TypedResults.NotFound(),
            RemoteErrorKind.Exists => Problem(StatusCodes.Status409Conflict, FileEndpoints.ExistsType),
            RemoteErrorKind.RateLimited => Problem(StatusCodes.Status429TooManyRequests, RateLimitedType),
            RemoteErrorKind.Refused => Problem(StatusCodes.Status403Forbidden, RefusedType),
            RemoteErrorKind.NotSupported => Problem(StatusCodes.Status409Conflict, NotSupportedType),
            RemoteErrorKind.WrongOffset => Problem(StatusCodes.Status409Conflict, null),
            RemoteErrorKind.TooLarge => Problem(StatusCodes.Status413PayloadTooLarge, null),
            RemoteErrorKind.InvalidName => Problem(StatusCodes.Status400BadRequest, null),
            _ => Problem(StatusCodes.Status502BadGateway, UnavailableType),
        };

        // The message names no token or address (RemoteStorageException's rule).
        IResult Problem(int status, string? type) =>
            TypedResults.Problem(error.Message, statusCode: status, type: type);
    }
}
