namespace Sfe.Core.Files;

/// <summary>How many entries of the trash someone sees, counted up to <see cref="Trash.CountCap"/>.</summary>
/// <param name="More">There are more than <see cref="Count"/>.</param>
public sealed record TrashCount(int Count, bool More);
