namespace Otools.Net.Sfe.Core.Files;

/// <summary>What a zip holds, checked: <see cref="Archives.WriteAsync"/> writes it.</summary>
public sealed record ArchivePlan(IReadOnlyList<ArchiveItem> Items, string FileName);
