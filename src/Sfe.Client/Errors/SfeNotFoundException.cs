namespace Otools.Net.Sfe.Client;

/// <summary>404: the file, folder or job isn't there, or the caller may not see it.</summary>
public sealed class SfeNotFoundException : SfeApiException
{
    /// <summary>A 404 with the problem read from the answer.</summary>
    /// <param name="statusCode">The HTTP status of the answer.</param>
    /// <param name="problem">The problem of its body.</param>
    public SfeNotFoundException(int statusCode, SfeProblem problem)
        : base(statusCode, problem) { }
}
