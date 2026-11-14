using Otools.Net.Sfe.Api.Maintenance;

namespace Otools.Net.Sfe.Api.Endpoints;

public sealed record InstallationCheck(string? Code);

/// <param name="Token">Sent back in the <c>X-SFE-Installation</c> header by the other installation routes, for an
/// hour.</param>
public sealed record InstallationGrant(string Token, InstallationView Settings);

/// <param name="RestartRequired">The new listening address applies once SFE is started again.</param>
public sealed record InstallationSaved(InstallationView Settings, bool RestartRequired);

/// <param name="Path">Full path of a folder on the server.</param>
public sealed record ServerFolder(string Name, string Path);

/// <param name="Path">The folder listed; null for the top (drives, or <c>/</c>).</param>
/// <param name="Parent">Its parent; null at the top of a drive.</param>
/// <param name="Places">At the top: the home folder, the current library folder and the usual media folders.</param>
/// <param name="Truncated">More sub-folders than <see cref="InstallationEndpoints.MaxFolders"/>: only the first ones
/// are listed.</param>
public sealed record ServerFolders(
    string? Path,
    string? Parent,
    IReadOnlyList<ServerFolder> Folders,
    IReadOnlyList<ServerFolder> Places,
    bool Truncated
);
