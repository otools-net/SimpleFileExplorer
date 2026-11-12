namespace Sfe.Core.Access;

public sealed record DirectoryResult(DirectoryStatus Status, DirectoryUser? User = null, DateTime? LockedUntil = null);
