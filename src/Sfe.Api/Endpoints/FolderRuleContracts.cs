using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Name">The group's name, or the account's display or user name.</param>
/// <param name="Exists">The folder is still there (false once it was moved, renamed or deleted outside SFE).</param>
public sealed record FolderRuleView(
    int Id,
    string Path,
    int? GroupId,
    int? UserId,
    string Name,
    FolderLevel Level,
    bool Exists,
    DateTime Created
);

/// <param name="Path">Folder, relative to the library root (empty: the root).</param>
/// <param name="GroupId">The group the rule is for; or <paramref name="UserId"/> for one account.</param>
/// <param name="Level">The level there; null removes the rule (the folder inherits again).</param>
public sealed record FolderRuleChange(string? Path, int? GroupId, int? UserId, FolderLevel? Level);

/// <param name="From">Where the rules are now: a folder that is no longer there.</param>
/// <param name="To">The folder they now apply to.</param>
public sealed record FolderRuleRepoint(string? From, string? To);

/// <summary>A group of the space, or one of its accounts, at a folder.</summary>
/// <param name="Level">
/// A group's level there (from its nearest rule, Write without one); an account's own rule's, null without one (its
/// groups decide).
/// </param>
/// <param name="Explicit">The level comes from a rule on this very folder.</param>
/// <param name="Sees">What it all comes to: it may browse the folder (accounts: with all their groups and
/// rules).</param>
/// <param name="ChangesFiles">It may change the folder's files (a right such as editing pictures, and Write
/// here).</param>
public sealed record FolderAccessEntry(
    int? GroupId,
    int? UserId,
    string Name,
    FolderLevel? Level,
    bool Explicit,
    bool Sees,
    bool ChangesFiles
);

/// <param name="Visitors">Visitors without an account (the Guests group), when the space lets them in; null
/// otherwise.</param>
public sealed record FolderAccessReport(
    string Path,
    bool Exists,
    IReadOnlyList<FolderAccessEntry> Groups,
    IReadOnlyList<FolderAccessEntry> Accounts,
    FolderAccessEntry? Visitors
);
