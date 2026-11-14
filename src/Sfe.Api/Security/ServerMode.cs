namespace Otools.Net.Sfe.Api.Security;

/// <summary>How the server is used, chosen at the setup (<see cref="ServerModes"/>).</summary>
public enum ServerMode
{
    /// <summary>On this computer, by its administrators: the portable app.</summary>
    Local,

    /// <summary>
    /// Opened by other people over the network: library paths and ffmpeg come from the configuration only.
    /// </summary>
    External,
}
