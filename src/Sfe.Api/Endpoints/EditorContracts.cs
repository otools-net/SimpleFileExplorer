namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Version">The picture's version (also the ETag): what saving over it will say it changes.</param>
/// <param name="Overwrite">The caller may save over it: <c>files.edit</c> at its folder and the rule of move/copy's
/// Replace, and a JPEG, PNG or WebP file (the formats the editor writes).</param>
/// <param name="Permanent">The space deletes for good: no previous version is kept in the trash.</param>
public sealed record EditedImageAccess(string Version, bool Overwrite, bool Permanent);

/// <summary>The picture saved over: its size in pixels and bytes, its new version (also the ETag) and time.</summary>
/// <param name="TrashId">The previous version's trash entry, when this save kept one.</param>
public sealed record SavedImage(
    string Path,
    int Width,
    int Height,
    long Size,
    string Version,
    DateTime Modified,
    int? TrashId
);
