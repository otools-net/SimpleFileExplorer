namespace Sfe.Core.Indexing;

/// <summary>Width and height in pixels, as the picture is shown (rotated by its orientation metadata).</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    /// <summary>
    /// Stored for a file whose size could not be read, so that it is not read again until it changes.
    /// </summary>
    public static readonly PixelSize Unknown = new(0, 0);

    public bool IsKnown => Width > 0 && Height > 0;
}
