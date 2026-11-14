using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.KeepAlive;

/// <summary>How keep alive goes, as a whole: what the indicator of the top bar shows.</summary>
public enum KeepAliveState
{
    /// <summary>Off, or nothing pinged yet (with <see cref="KeepAliveMode.Auto"/>: no folder on the network).</summary>
    None,

    /// <summary>Every folder pinged answered its last ping.</summary>
    Ok,

    /// <summary>A folder did not answer its last ping.</summary>
    Failing,
}
