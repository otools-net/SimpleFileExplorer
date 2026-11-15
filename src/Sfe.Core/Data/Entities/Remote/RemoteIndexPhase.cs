namespace Otools.Net.Sfe.Core.Data;

/// <summary>Where the index of a connection stands (<see cref="RemoteIndexState"/>).</summary>
public enum RemoteIndexPhase
{
    /// <summary>A full listing goes folder by folder (the first one, or after the provider reset its cursor).</summary>
    Listing,

    /// <summary>Listed: the provider's change cursor keeps it current (without one, each sync lists again).</summary>
    Following,
}
