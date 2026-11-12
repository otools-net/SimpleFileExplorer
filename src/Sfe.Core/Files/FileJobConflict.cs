namespace Sfe.Core.Files;

/// <param name="Source">What is moved or copied.</param>
/// <param name="Existing">What is already there under that name.</param>
/// <param name="CanReplace">The person may replace it (it would go to the trash).</param>
public sealed record FileJobConflict(ClashSide Source, ClashSide Existing, bool CanReplace);
