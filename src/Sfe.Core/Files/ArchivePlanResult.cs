namespace Sfe.Core.Files;

public sealed record ArchivePlanResult(FileOperationStatus Status, ArchivePlan? Plan, string? Message)
{
    public static ArchivePlanResult Fail(FileOperationStatus status, string message) => new(status, null, message);
}
