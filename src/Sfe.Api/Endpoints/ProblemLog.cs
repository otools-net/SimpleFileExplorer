using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sfe.Api.Security;

namespace Sfe.Api.Endpoints;

/// <summary>
/// Every answer of the API that explains a refusal or a failure (a problem body: 400 and its field errors, 409, 413,
/// 422, 5xx) goes into the server log (agents/docs/features/logs.md), with the route and who asked: what the person
/// was told can be found again. 400 is a warning (a request the server refused as malformed: a client and server
/// disagreeing), 5xx an error, the rest information. Plain 401/403/404 (no body) are not logged here. The path only,
/// never the query string (unlock ids) or the body (passwords); problems never hold a password.
/// </summary>
public static class ProblemLog
{
    public const string Category = "Sfe.Api.Problems";

    public static RouteGroupBuilder LogProblems(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            try
            {
                await LogAsync(context.HttpContext, result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The answer goes out whatever the log does.
            }
            return result;
        });
        return group;
    }

    private static async Task LogAsync(HttpContext http, object? result)
    {
        while (result is INestedHttpResult nested)
            result = nested.Result;
        if (result is not IStatusCodeHttpResult { StatusCode: >= 400 and var status } || result is not IValueHttpResult { Value: ProblemDetails problem })
            return;

        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        var level = status switch
        {
            >= 500 => LogLevel.Error,
            400 => LogLevel.Warning,
            _ => LogLevel.Information,
        };
        if (!logger.IsEnabled(level))
            return;
        var who = http.RequestServices.GetService<CurrentAccess>() is { } current ? (await current.GetAsync()).User?.UserName : null;
        logger.Log(level, "{Method} {Path} answered {Status} to {User}: {Problem}",
            http.Request.Method, http.Request.Path.Value, status, who ?? "a visitor", Describe(problem));
    }

    /// <summary>The problem's words: its detail or title, and each field's errors.</summary>
    public static string Describe(ProblemDetails problem)
    {
        var text = problem.Detail ?? problem.Title ?? "no detail";
        if (problem is HttpValidationProblemDetails { Errors.Count: > 0 } validation)
            text += " " + string.Join("; ", validation.Errors.Select(field => $"{field.Key}: {string.Join(" ", field.Value)}"));
        return text;
    }
}
