namespace Sfe.Core.Access;

/// <summary>
/// One principal's part in a subject's folder access: a group the subject is in, or the subject's own account.
/// </summary>
/// <param name="Rules">Its folder rules by root-relative path.</param>
/// <param name="Rights">The rights it grants: a group's own; for an account, those of all its groups in the
/// library.</param>
/// <param name="UnrestrictedByDefault">
/// True for a group: where no rule of it is above a path, it has <see cref="FolderLevel.Write"/> there (what every
/// group had before folder rules). False for an account: without a rule of its own it has no say.
/// </param>
public sealed record FolderPrincipal(
    IReadOnlyDictionary<string, FolderLevel> Rules,
    IReadOnlySet<string> Rights,
    bool UnrestrictedByDefault
);
