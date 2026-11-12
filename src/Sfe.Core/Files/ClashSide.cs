namespace Sfe.Core.Files;

/// <summary>An item on one side of a name clash.</summary>
public sealed record ClashSide(string Name, bool IsFolder, long? Size, DateTimeOffset? Modified);
