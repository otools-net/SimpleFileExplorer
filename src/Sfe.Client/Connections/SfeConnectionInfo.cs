namespace Otools.Net.Sfe.Client;

/// <summary>A connection (storage outside the libraries: Dropbox…) the caller may use in its library.</summary>
public sealed class SfeConnectionInfo
{
    /// <summary>Its id (<c>sfe.Connections[id]</c>).</summary>
    public int Id { get; init; }

    /// <summary>The provider's kind (<c>dropbox</c>).</summary>
    public string Kind { get; init; } = "";

    /// <summary>Its name, as shown in the app.</summary>
    public string Name { get; init; } = "";

    /// <summary>Whose it is.</summary>
    public SfeConnectionLevel Level { get; init; }

    /// <summary>Whether its sign-in still works.</summary>
    public SfeConnectionState State { get; init; }

    /// <summary>How far the caller uses it.</summary>
    public SfeConnectionAccess Access { get; init; }

    /// <summary>What its provider can do; null when the server doesn't tell it for this kind.</summary>
    public SfeConnectionCapabilities? Capabilities { get; init; }
}
