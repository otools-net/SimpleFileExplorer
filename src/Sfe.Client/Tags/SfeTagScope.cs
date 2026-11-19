namespace Otools.Net.Sfe.Client;

/// <summary>
/// The files a scope change of tags covers (<see cref="SfeConnectionTags.ChangeScopeAsync"/>): those of a folder, of a
/// folder and its sub-folders, or those a search finds in a folder.
/// </summary>
public sealed class SfeTagScope
{
    /// <summary>The folder; <c>""</c> (the default): the root.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Without <see cref="Query"/>: whether the files of its sub-folders are covered too.</summary>
    public bool Recursive { get; set; }

    /// <summary>
    /// A search, as typed in the app: the scope is then what it finds in <see cref="Folder"/> and below; null (the
    /// default): the folder's files.
    /// </summary>
    public string? Query { get; set; }
}
