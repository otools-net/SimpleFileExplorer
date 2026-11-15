using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// The server refused a call: its status, and the problem it answered (<c>type</c> word, <c>title</c>, <c>detail</c>).
/// Programs test <see cref="StatusCode"/> and <see cref="ProblemType"/>; <see cref="Detail"/> is for people.
/// </summary>
public class SfeApiException : SfeException
{
    /// <summary>A refusal with status <paramref name="statusCode"/> and the problem read from the answer.</summary>
    /// <param name="statusCode">The HTTP status of the answer.</param>
    /// <param name="problem">The problem of its body; empty when it had none.</param>
    public SfeApiException(int statusCode, SfeProblem problem)
        : base(MessageOf(statusCode, problem))
    {
        StatusCode = statusCode;
        ProblemType = problem.Word;
        Title = problem.Title;
        Detail = problem.Detail;
        Errors = problem.Errors;
    }

    /// <summary>The HTTP status of the answer (404, 409…).</summary>
    public int StatusCode { get; }

    /// <summary>
    /// The problem's <c>type</c> when it is one of the API's words (<c>file-exists</c>, <c>scan-running</c>…, listed
    /// in the API reference); null for a problem without a case of its own.
    /// </summary>
    public string? ProblemType { get; }

    /// <summary>The problem's short title, when the answer had one.</summary>
    public string? Title { get; }

    /// <summary>What happened, for people; may change between versions.</summary>
    public string? Detail { get; }

    /// <summary>For a refused input (400), the reasons by field name; empty otherwise.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    private static string MessageOf(int statusCode, SfeProblem problem)
    {
        var what = problem.Detail ?? problem.Title ?? "The server refused the call.";
        return problem.Word is { } word ? $"{statusCode} {word}: {what}" : $"{statusCode}: {what}";
    }
}
