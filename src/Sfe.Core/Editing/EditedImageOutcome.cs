using Sfe.Core.Files;
using Sfe.Core.Options;

namespace Sfe.Core.Editing;

public enum EditedImageOutcome
{
    Saved,

    /// <summary>The original is not a picture of the library (missing, outside the root, not an image type).</summary>
    SourceNotFound,

    /// <summary>What was sent is not a JPEG, PNG or WebP picture whose size can be read.</summary>
    InvalidImage,

    /// <summary>What was sent is larger than <see cref="EditedImages.MaxBytes"/>.</summary>
    TooLarge,

    /// <summary>
    /// <see cref="EditorOptions.OutputFolder"/> is not a folder of the library, or the file cannot be written there.
    /// </summary>
    CannotWrite,

    /// <summary>
    /// Saved over the original: the edited picture is not in the original's format (a GIF made a PNG...).
    /// </summary>
    OtherFormat,

    /// <summary>
    /// Saved over the original: <see cref="FileContent.OverwriteAsync"/> did not (see
    /// <see cref="EditedImageResult.Written"/>).
    /// </summary>
    NotWritten,
}
