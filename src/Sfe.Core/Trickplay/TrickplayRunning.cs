namespace Otools.Net.Sfe.Core.Trickplay;

/// <summary>A video being processed, since <paramref name="Started"/>.</summary>
public sealed record TrickplayRunning(string Path, DateTimeOffset Started);
