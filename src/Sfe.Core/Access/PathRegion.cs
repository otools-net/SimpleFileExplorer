namespace Sfe.Core.Access;

/// <param name="Path">A folder where the right applies, with everything below it...</param>
/// <param name="Except">...but these folders below it (and what is below them).</param>
public sealed record PathRegion(string Path, IReadOnlyList<string> Except);
