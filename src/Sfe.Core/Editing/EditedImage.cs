namespace Otools.Net.Sfe.Core.Editing;

/// <param name="Path">The new file, relative to the library root.</param>
public sealed record EditedImage(string Path, int Width, int Height, long Size);
