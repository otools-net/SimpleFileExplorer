using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// The problem details (RFC 9457) of a refusal, as the server wrote them: the parts of an
/// <see cref="SfeApiException"/>.
/// </summary>
public sealed class SfeProblem
{
    /// <summary>
    /// The <c>type</c>: one of the API's words (<c>file-exists</c>…), or a link to the definition of the status.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>A short title of the problem.</summary>
    public string? Title { get; init; }

    /// <summary>What happened, for people.</summary>
    public string? Detail { get; init; }

    /// <summary>For a refused input, the reasons by field name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();

    /// <summary>The <see cref="Type"/> when it is one of the API's words rather than a link.</summary>
    internal string? Word => Type is { Length: > 0 } type && type.IndexOf(':') < 0 ? type : null;

    /// <summary>A problem with nothing in it: an answer without a problem body.</summary>
    internal static SfeProblem Empty { get; } = new();
}
