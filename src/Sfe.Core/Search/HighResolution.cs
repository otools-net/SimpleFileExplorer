namespace Otools.Net.Sfe.Core.Search;

/// <summary>What makes an image or video high resolution (<c>r:hr</c>, <c>r:lr</c>, the HR/LR marks).</summary>
/// <param name="ImageExtensions">Extensions classified as images (without dot).</param>
/// <param name="ImagePixels">Smallest shorter side of a high-resolution image.</param>
public sealed record HighResolution(
    IReadOnlyCollection<string> ImageExtensions,
    int ImagePixels,
    IReadOnlyCollection<string> VideoExtensions,
    int VideoPixels
);
