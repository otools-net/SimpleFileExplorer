namespace Sfe.Core.Access;

public sealed record FolderRuleInfo(
    int Id,
    string Path,
    FolderPrincipalRef Principal,
    FolderLevel Level,
    DateTime Created,
    int? CreatedById
);
