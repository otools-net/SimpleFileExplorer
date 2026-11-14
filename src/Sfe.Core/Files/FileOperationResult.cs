using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Files;

/// <param name="Item">The new or renamed file or folder.</param>
/// <param name="Message">Why it was not done, for the person (never a server path).</param>
/// <param name="TrashId">The trash entry of what was deleted to the trash (for an Undo).</param>
public sealed record FileOperationResult(
    FileOperationStatus Status,
    LibraryItem? Item = null,
    string? Message = null,
    int? TrashId = null
)
{
    public static FileOperationResult Fail(FileOperationStatus status, string message) => new(status, Message: message);
}
