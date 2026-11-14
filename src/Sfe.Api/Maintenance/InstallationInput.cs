using Otools.Net.Sfe.Api.Security;

namespace Otools.Net.Sfe.Api.Maintenance;

/// <param name="Root">The first library's folder; null keeps it.</param>
/// <param name="Network">Listen on every network interface (true) or on this computer only (false); null keeps
/// it.</param>
/// <param name="Port">The port; null keeps it.</param>
/// <param name="FfmpegPath">ffmpeg's executable or folder; empty = look for it on PATH; null keeps it.</param>
/// <param name="Mode">How the server is used; null keeps it.</param>
public sealed record InstallationInput(
    string? Root = null,
    bool? Network = null,
    int? Port = null,
    string? FfmpegPath = null,
    ServerMode? Mode = null
);
