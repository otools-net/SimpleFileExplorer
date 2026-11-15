namespace Otools.Net.Sfe.Client;

/// <summary>
/// The server this client calls lacks a function (an older version, or one turned off there or for this account):
/// thrown before calling it, rather than a refusal the program would have to read.
/// </summary>
public sealed class SfeNotSupportedException : SfeException
{
    /// <summary>The function <paramref name="function"/> is missing on a server of version
    /// <paramref name="serverVersion"/>.</summary>
    /// <param name="function">The function asked for.</param>
    /// <param name="serverVersion">The server's version, as it tells it.</param>
    public SfeNotSupportedException(SfeFunction function, string serverVersion)
        : base($"The server (version {serverVersion}) doesn't offer {function} to this account, or has it off.")
    {
        Function = function;
        ServerVersion = serverVersion;
    }

    /// <summary>The function missing.</summary>
    public SfeFunction Function { get; }

    /// <summary>The server's version, as it tells it.</summary>
    public string ServerVersion { get; }
}
