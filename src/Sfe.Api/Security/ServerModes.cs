using Sfe.Core.Options;

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
/// <remarks>
/// Local mode (once the setup is done; before it nobody but the setup gets in anyway): <see cref="RequestGuard"/> answers
/// <see cref="LocalOnlyType"/> to visitors on other computers, <see cref="SfeSession.ValidateAsync"/> and the sign-in let only
/// administrators (<see cref="Sfe.Core.Access.AccessService.IsAdministratorAsync"/>) in, and <see cref="ApplyTo"/> turns
/// anonymous guests off.
/// </remarks>
public static class ServerModes
{
    public const string Key = "Sfe:Mode";

    /// <summary>The problem type of a request refused because a Local server only answers its own computer.</summary>
    public const string LocalOnlyType = "local-only";

    /// <summary>The mode configured; null when nothing sets it. An unknown word counts as External, the stricter one.</summary>
    public static ServerMode? Configured(IConfiguration configuration) => configuration[Key]?.Trim() switch
    {
        null or "" => null,
        var word when Enum.TryParse<ServerMode>(word, ignoreCase: true, out var mode) && Enum.IsDefined(mode) && !char.IsDigit(word[0]) => mode,
        _ => ServerMode.External,
    };

    /// <summary>The mode in effect: an install whose configuration sets none (installs older than the modes) is Local.</summary>
    public static ServerMode InEffect(IConfiguration configuration) => Configured(configuration) ?? ServerMode.Local;

    public static bool IsLocal(IConfiguration configuration) => InEffect(configuration) == ServerMode.Local;

    /// <summary>
    /// A Local server has no visitors without an account: <see cref="SecurityOptions.AnonymousGuests"/> is off whatever
    /// the configuration says (re-applied when the mode changes: the options follow the configuration's reloads).
    /// </summary>
    public static void ApplyTo(IServiceCollection services, IConfiguration configuration) =>
        services.PostConfigure<SecurityOptions>(security =>
        {
            if (IsLocal(configuration))
                security.AnonymousGuests = false;
        });
}
