using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Core.Editing;

/// <param name="Written">Saved over the original: what <see cref="FileContent.OverwriteAsync"/> answered.</param>
public sealed record EditedImageResult(
    EditedImageOutcome Outcome,
    EditedImage? Image = null,
    string? Message = null,
    OverwriteResult? Written = null
);
