namespace Otools.Net.Sfe.Client;

/// <summary>
/// 409: the call doesn't fit the state of things: a name taken (<c>file-exists</c>), a scan running
/// (<c>scan-running</c>), folder rules that can't follow (<c>folder-rules</c>)…;
/// <see cref="SfeApiException.ProblemType"/> says which.
/// </summary>
public sealed class SfeConflictException : SfeApiException
{
    /// <summary>A 409 with the problem read from the answer.</summary>
    /// <param name="statusCode">The HTTP status of the answer.</param>
    /// <param name="problem">The problem of its body.</param>
    public SfeConflictException(int statusCode, SfeProblem problem)
        : base(statusCode, problem) { }
}
