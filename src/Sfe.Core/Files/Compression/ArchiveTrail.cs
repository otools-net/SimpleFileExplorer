namespace Otools.Net.Sfe.Core.Files.Compression;

/// <summary>
/// The end of the path to a folder: the root is always first, then the <c>Skipped</c> folders between, then
/// <c>Last</c>.
/// </summary>
public sealed record ArchiveTrail(int Skipped, IReadOnlyList<ArchiveFolder> Last);
