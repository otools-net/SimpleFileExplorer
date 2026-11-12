namespace Sfe.Core.Options;

/// <summary>
/// The web client's picture editor (crop...). The browser applies the changes and sends the result, which the server
/// saves as a new file: the original is never changed.
/// </summary>
public sealed class EditorOptions
{
    /// <summary>
    /// Library folder (relative to the root, separated by <c>/</c>) where edited pictures are saved, in the same
    /// sub-folders as their originals: <c>Photos/2024/beach.jpg</c> is saved as <c>Edited/Photos/2024/beach.jpg</c>.
    /// Empty = next to the original, as <c>beach (edited).jpg</c>. Created when first needed.
    /// </summary>
    public string? OutputFolder { get; set; } = "Edited";

    /// <summary>Quality (1-100) of the JPEG and WebP pictures the editor saves; PNG pictures are lossless.</summary>
    public int Quality { get; set; } = 92;
}
