namespace Otools.Net.Sfe.Core.Files;

/// <summary>One file or folder chosen, with the name it gets at the top of the zip.</summary>
public sealed record ArchiveItem(string Path, string FullPath, string RealPath, bool IsFolder, string EntryName);
