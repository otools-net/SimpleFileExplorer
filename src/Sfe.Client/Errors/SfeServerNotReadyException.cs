namespace Otools.Net.Sfe.Client;

/// <summary>
/// The server can't serve programs yet: it has no administrator (<c>setup-required</c>), waits for its update to be
/// started from the app (<c>migration-required</c>), or is a Local server answering only its own computer
/// (<c>local-only</c>). A person must act there: retrying doesn't help.
/// </summary>
public sealed class SfeServerNotReadyException : SfeApiException
{
    /// <summary>A refusal of a server not ready, with the problem read from the answer.</summary>
    /// <param name="statusCode">The HTTP status of the answer (503, or 403 for <c>local-only</c>).</param>
    /// <param name="problem">The problem of its body.</param>
    public SfeServerNotReadyException(int statusCode, SfeProblem problem)
        : base(statusCode, problem) { }

    /// <summary>Whether <paramref name="word"/> is the problem word of a server not ready.</summary>
    internal static bool IsNotReady(string? word) => word is "setup-required" or "migration-required" or "local-only";
}
