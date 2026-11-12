using Sfe.Core.Files;

namespace Sfe.Core.Editing;

/// <param name="Written">Saved over the original: what <see cref="FileContent.OverwriteAsync"/> answered.</param>
public sealed record EditedImageResult(
    EditedImageOutcome Outcome,
    EditedImage? Image = null,
    string? Message = null,
    OverwriteResult? Written = null
);
