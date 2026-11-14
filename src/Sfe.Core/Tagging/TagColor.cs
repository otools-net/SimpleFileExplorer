namespace Otools.Net.Sfe.Core.Tagging;

/// <param name="Slug">Value stored in <see cref="Data.Tag.Color"/>.</param>
/// <param name="Hex">Background color; the client picks a readable text color for it.</param>
public sealed record TagColor(string Slug, string Name, string Hex);
