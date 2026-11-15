using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client;

/// <summary>Turns a refused answer into the <see cref="SfeApiException"/> of its case.</summary>
internal static class ProblemReader
{
    /// <summary>The problem of <paramref name="response"/>'s body; empty when it has none or isn't JSON.</summary>
    public static async Task<SfeProblem> ReadAsync(HttpResponseMessage response)
    {
        var body = response.Content is null ? "" : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (body.Length == 0)
            return SfeProblem.Empty;
        try
        {
            return JsonSerializer.Deserialize<SfeProblem>(body, SfeJson.Options) ?? SfeProblem.Empty;
        }
        catch (JsonException)
        {
            // A proxy's HTML page, say: the status alone tells the case.
            return SfeProblem.Empty;
        }
    }

    /// <summary>The exception of a refusal: by its problem word first, then by its status.</summary>
    public static SfeApiException ToException(int statusCode, SfeProblem problem, TimeSpan? retryAfter)
    {
        if (SfeServerNotReadyException.IsNotReady(problem.Word))
            return new SfeServerNotReadyException(statusCode, problem);
        return statusCode switch
        {
            404 => new SfeNotFoundException(statusCode, problem),
            401 or 403 => new SfeRefusedException(statusCode, problem),
            409 => new SfeConflictException(statusCode, problem),
            429 => new SfeRateLimitedException(statusCode, problem, retryAfter),
            _ => new SfeApiException(statusCode, problem),
        };
    }
}
