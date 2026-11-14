namespace Otools.Net.Sfe.Core.Files;

/// <param name="Version">The new version (Done), or the one on the disk now (Changed).</param>
/// <param name="TrashId">The previous version's trash entry, when this save kept one.</param>
/// <param name="Message">Why it was not done, for the person (never a server path).</param>
public sealed record OverwriteResult(
    OverwriteStatus Status,
    FileVersion? Version = null,
    int? TrashId = null,
    string? Message = null
);
