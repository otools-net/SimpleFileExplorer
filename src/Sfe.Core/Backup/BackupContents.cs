namespace Otools.Net.Sfe.Core.Backup;

/// <param name="Version">
/// The version the databases belong to, which a restore needs (before an update, the earlier one); null = an install
/// from before 1.37.0, which recorded none.
/// </param>
/// <param name="MadeBy">The version of SFE making the backup.</param>
/// <param name="Notes">Said in the manifest and the readme (e.g. a library whose database could not be found).</param>
public sealed record BackupContents(
    string? Version,
    string MadeBy,
    IReadOnlyList<BackupDatabase> Databases,
    IReadOnlyList<BackupFile> Files,
    IReadOnlyList<string> Notes
);
