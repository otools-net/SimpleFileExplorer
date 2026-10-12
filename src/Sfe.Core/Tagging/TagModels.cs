namespace Sfe.Core.Tagging;

/// <summary>What a tag chip needs.</summary>
/// <param name="DisplayName">Name followed by the disambiguation parent, e.g. "Freddy (FNAF)".</param>
public sealed record TagSummary(int Id, string Name, string DisplayName, string? Color, bool IsCategory, bool IsHidden);

public sealed record TagDetails(
    int Id,
    string Name,
    string? Shorthand,
    string DisplayName,
    string? Color,
    bool IsCategory,
    bool IsHidden,
    bool IsBuiltIn,
    int? DisambiguationId,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<TagSummary> Parents,
    int FileCount);

/// <summary>Values of a new or edited tag.</summary>
/// <param name="DisambiguationId">Must be one of <paramref name="ParentIds"/>.</param>
public sealed record TagInput(
    string Name,
    string? Shorthand = null,
    string? Color = null,
    bool IsCategory = false,
    bool IsHidden = false,
    int? DisambiguationId = null,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<int>? ParentIds = null);

/// <param name="Matches">Tags whose name, shorthand or alias contains the text, best matches first.</param>
/// <param name="Descendants">Tags inheriting from a match (they are found by a search for the match too).</param>
public sealed record TagSearchResults(IReadOnlyList<TagDetails> Matches, IReadOnlyList<TagDetails> Descendants);

/// <param name="Category">Category tag the group is named after; null for the default "Tags" group.</param>
public sealed record TagGroup(TagSummary? Category, IReadOnlyList<TagSummary> Tags);

/// <param name="Tags">Tags set on the file (not the ones it inherits through parents).</param>
/// <param name="Groups">The same tags grouped by category, like TagStudio's inspector.</param>
public sealed record FileTags(string Path, IReadOnlyList<TagSummary> Tags, IReadOnlyList<TagGroup> Groups);

/// <param name="Paths">Files, relative to the library root.</param>
public sealed record FileTagChange(IReadOnlyList<string> Paths, IReadOnlyList<int>? Add = null, IReadOnlyList<int>? Remove = null);

public sealed record FileTagChangeResult(int Added, int Removed, IReadOnlyList<FileTags> Files);

/// <param name="FileCount">Files tagged with it directly.</param>
public sealed record TagUsage(TagSummary Tag, int FileCount);

/// <summary>Tag lists of the client's sidebar.</summary>
/// <param name="Popular">Tags on the most files.</param>
/// <param name="Searched">Tags most recently named by a search.</param>
/// <param name="Viewed">Tags of the most recently viewed files.</param>
/// <param name="Lucky">Random tags in none of the other lists.</param>
/// <param name="HistoryEnabled">Searched and viewed tags are recorded (when off, those lists are empty).</param>
public sealed record TagSidebar(
    IReadOnlyList<TagUsage> Popular,
    IReadOnlyList<TagUsage> Searched,
    IReadOnlyList<TagUsage> Viewed,
    IReadOnlyList<TagUsage> Lucky,
    bool HistoryEnabled);

public enum TagEditStatus
{
    Ok,
    NotFound,
    Invalid,
}

public sealed record TagEditResult(TagEditStatus Status, TagDetails? Tag = null, IDictionary<string, string[]>? Errors = null)
{
    public static TagEditResult NotFound { get; } = new(TagEditStatus.NotFound);

    public static TagEditResult Invalid(string field, string message) =>
        new(TagEditStatus.Invalid, Errors: new Dictionary<string, string[]> { [field] = [message] });
}

public enum TagDeleteStatus
{
    Deleted,
    NotFound,
    BuiltIn,
}

/// <summary>Files tagged together without listing them: those of a folder, or the matches of a search.</summary>
/// <param name="Path">The folder, relative to the library root; for a search, the folder searched in.</param>
/// <param name="Recursive">A folder's sub-folders are included (a search always looks in them).</param>
/// <param name="Query">The search; null or blank for the files of the folder.</param>
public sealed record TagScope(string? Path, bool Recursive = false, string? Query = null)
{
    public bool IsSearch => !string.IsNullOrWhiteSpace(Query);
}

/// <param name="Scope">The scope as understood: path normalised, <c>Recursive</c> false for a search.</param>
/// <param name="Files">Indexed files of the scope.</param>
/// <param name="Tags">Tags set on at least one of them, with the number of files having each; most common first.</param>
public sealed record ScopeTags(TagScope Scope, int Files, IReadOnlyList<TagUsage> Tags);

/// <summary>Tags to add and remove on every file of a <see cref="TagScope"/>.</summary>
public sealed record ScopeTagChange(
    string? Path,
    bool Recursive = false,
    string? Query = null,
    IReadOnlyList<int>? Add = null,
    IReadOnlyList<int>? Remove = null);

/// <param name="Files">Files of the scope (all were given the added tags).</param>
/// <param name="Added">Tags set on files that didn't have them.</param>
/// <param name="Removed">Tags taken off files that had them.</param>
public sealed record ScopeTagChangeResult(TagScope Scope, int Files, int Added, int Removed);

public enum ScopeTagStatus
{
    Ok,
    NotFound,
    Invalid,
    /// <summary>A folder can't be indexed (to be tagged) while a scan runs.</summary>
    ScanRunning,
}

public sealed record ScopeTagOutcome(ScopeTagStatus Status, ScopeTagChangeResult? Result = null, IDictionary<string, string[]>? Errors = null)
{
    public static ScopeTagOutcome NotFound { get; } = new(ScopeTagStatus.NotFound);

    public static ScopeTagOutcome Invalid(string field, string message) =>
        new(ScopeTagStatus.Invalid, Errors: new Dictionary<string, string[]> { [field] = [message] });
}
