namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>How the server runs, so that the setup page shows how to give it a setup code there first.</summary>
public enum HostingKind
{
    WindowsService,
    Systemd,
    Iis,
    Docker,
    AppImage,
    Portable,
    Other,
}

/// <param name="Hosting">How the server runs (the setup page explains how to set the code for it).</param>
/// <param name="CodeGenerated">The code is the one the server made itself: shown in its console and kept in
/// <paramref name="CodeFile"/>.</param>
/// <param name="CodeRequired">
/// The setup asks this visitor for the code; false for a new install set up from the server's own computer
/// (<c>Sfe:Setup:LocalWithoutCode</c>): the setup and installation routes then take no code.
/// </param>
public sealed record SetupInfo(
    bool Required,
    bool Available,
    bool CodeConfigured,
    bool CodeTooShort,
    bool Recover,
    int MinimumCodeLength,
    int MinimumPasswordLength,
    string LibraryName,
    HostingKind Hosting,
    bool CodeGenerated = false,
    string? CodeFile = null,
    bool CodeRequired = true
);
