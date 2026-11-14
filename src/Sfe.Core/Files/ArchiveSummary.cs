namespace Otools.Net.Sfe.Core.Files;

/// <param name="Files">Files written.</param>
/// <param name="Bytes">Their size.</param>
/// <param name="LeftOut">Root-relative paths left out (links, unreadable files, folders not downloadable), listed in
/// the zip.</param>
public sealed record ArchiveSummary(int Files, long Bytes, IReadOnlyList<string> LeftOut);
