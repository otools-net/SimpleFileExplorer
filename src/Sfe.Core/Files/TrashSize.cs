namespace Otools.Net.Sfe.Core.Files;

/// <summary>What the whole trash holds, as the index knew its entries when they were deleted.</summary>
/// <param name="Bytes">The sizes of the entries the index knew.</param>
/// <param name="Unknown">Entries whose size the index didn't know (not indexed when deleted).</param>
public sealed record TrashSize(long Bytes, int Unknown);
