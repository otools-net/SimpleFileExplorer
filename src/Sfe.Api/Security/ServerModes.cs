namespace Sfe.Api.Security;

/// <summary>How the server is used, chosen at the setup (<see cref="ServerModes"/>).</summary>
public enum ServerMode
{
    /// <summary>On this computer, by its administrators: the portable app.</summary>
    Local,

    /// <summary>Opened by other people over the network: library paths and ffmpeg come from the configuration only.</summary>
    External,
}

/// <summary>
/// The server's mode, <c>Sfe:Mode</c>: written by the setup into the installation file, or set by appsettings, the
/// environment (<c>Sfe__Mode</c>) or the command line, which then lock it. Read from the configuration as a word, not
/// bound into <see cref="Sfe.Core.Options.SfeOptions"/>: the binder would throw on a misspelled value and stop the server.
/// </summary>
public static class ServerModes
{
    public const string Key = "Sfe:Mode";

    /// <summary>The mode configured; null when nothing sets it. An unknown word counts as External, the stricter one.</summary>
    public static ServerMode? Configured(IConfiguration configuration) => configuration[Key]?.Trim() switch
    {
        null or "" => null,
        var word when Enum.TryParse<ServerMode>(word, ignoreCase: true, out var mode) && Enum.IsDefined(mode) && !char.IsDigit(word[0]) => mode,
        _ => ServerMode.External,
    };

    /// <summary>The mode in effect: an install whose configuration sets none (installs older than the modes) is Local.</summary>
    public static ServerMode InEffect(IConfiguration configuration) => Configured(configuration) ?? ServerMode.Local;
}
