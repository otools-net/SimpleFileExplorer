namespace Sfe.Core.Access;

/// <summary>
/// What a subject may do at one path: the rights it was granted in the library that the folder lets through.
/// </summary>
public sealed class FolderAccess
{
    private readonly IReadOnlySet<string> _granted;
    private readonly IReadOnlySet<string>? _atFolder;

    internal FolderAccess(IReadOnlySet<string> granted, IReadOnlySet<string>? atFolder, bool pathOnly)
    {
        _granted = granted;
        _atFolder = atFolder;
        PathOnly = pathOnly;
    }

    /// <summary>
    /// The subject can't browse the folder but can browse something below it: the folder is shown only to lead there,
    /// with nothing of its own (no files, counts, covers).
    /// </summary>
    public bool PathOnly { get; }

    /// <summary>The folder is shown to the subject, browsable or only leading to what is.</summary>
    public bool IsVisible => Has(Rights.Browse) || PathOnly;

    public bool Has(string right) => _granted.Contains(right) && (_atFolder is null || _atFolder.Contains(right));

    /// <summary>
    /// What both allow: one place reached by two paths (a link and its target) gets the stricter access.
    /// </summary>
    public FolderAccess And(FolderAccess other) =>
        _atFolder is null && other._atFolder is null
            ? this
            : new FolderAccess(_granted, Allowed.Where(other.Has).ToHashSet(), PathOnly && other.PathOnly);

    /// <summary>The rights the subject has here.</summary>
    public IEnumerable<string> Allowed => _atFolder is null ? _granted : _granted.Where(_atFolder.Contains);
}
