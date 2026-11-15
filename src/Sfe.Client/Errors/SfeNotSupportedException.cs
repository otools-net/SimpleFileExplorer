namespace Otools.Net.Sfe.Client;

/// <summary>
/// The server this client calls lacks a function (an older version, or one turned off there or for this account),
/// or a connection's provider can't do what is asked (<see cref="Capability"/>): thrown before calling it, rather
/// than a refusal the program would have to read.
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

    /// <summary>Connection <paramref name="connectionId"/>'s provider lacks <paramref name="capability"/>.</summary>
    /// <param name="capability">What the call needs of the provider.</param>
    /// <param name="connectionId">The connection's id.</param>
    public SfeNotSupportedException(SfeConnectionCapability capability, int connectionId)
        : base($"The provider of connection {connectionId} can't do this: it lacks {capability}.")
    {
        Function = SfeFunction.Connections;
        Capability = capability;
    }

    /// <summary>The function missing (<see cref="SfeFunction.Connections"/> for a provider's capability).</summary>
    public SfeFunction Function { get; }

    /// <summary>The server's version, as it tells it; null when a provider's capability is missing.</summary>
    public string? ServerVersion { get; }

    /// <summary>What a connection's provider can't do; null when the server lacks a function.</summary>
    public SfeConnectionCapability? Capability { get; }
}
