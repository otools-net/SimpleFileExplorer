using Sfe.Api.Security;

namespace Sfe.Api.Maintenance;

/// <param name="Root">The first library's folder (<c>Sfe:Root</c>).</param>
/// <param name="RootExists">That folder exists on the server.</param>
/// <param name="Urls">The listening addresses (<c>Urls</c>).</param>
/// <param name="Port">The port of the first address.</param>
/// <param name="Network">The first address listens on every network interface, not only this computer's.</param>
/// <param name="FfmpegPath">ffmpeg as configured (<c>Sfe:Thumbnails:FfmpegPath</c>).</param>
/// <param name="FfmpegFound">The executable found for it; null when there is none.</param>
/// <param name="FfmpegVersion">The version it reports.</param>
/// <param name="RestartRequired">The addresses saved differ from those the server listens on since it started.</param>
/// <param name="Mode"><c>Sfe:Mode</c> as configured; a null value = not chosen yet (the server then works as
/// Local).</param>
/// <param name="ModeInEffect">The mode the server works in (<see cref="ServerModes.InEffect"/>).</param>
/// <param name="OnThisComputer">The visitor is on the server's own computer (<see cref="LocalVisitor"/>): only they may
/// choose Local.</param>
public sealed record InstallationView(
    InstallationField Root,
    bool RootExists,
    InstallationField Urls,
    int Port,
    bool Network,
    InstallationField FfmpegPath,
    string? FfmpegFound,
    string? FfmpegVersion,
    bool RestartRequired,
    InstallationField Mode,
    ServerMode ModeInEffect,
    bool OnThisComputer = false
);
