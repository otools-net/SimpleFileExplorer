namespace Sfe.Core.Files.Compression;

/// <summary>An entry not extracted, and why.</summary>
public sealed record ExtractLeftOut(string Name, string Reason);
