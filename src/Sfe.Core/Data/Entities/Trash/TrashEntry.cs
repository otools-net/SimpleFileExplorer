using Sfe.Core.Options;

namespace Sfe.Core.Data;

/// <summary>
/// A file or folder deleted to the trash (agents/docs/features/trash.md): it is kept in the work folder
/// (<see cref="Files.WorkFolder"/>) under <c>trash/{Folder}/{Name}</c> until it is restored, deleted for good or purged.
/// </summary>
public sealed class TrashEntry
{
    public int Id { get; set; }

    /// <summary>Where it was, relative to the library root ('/' separated).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Its name (the last part of <see cref="Path"/>), also its name in the trash.</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    /// <summary>Bytes, as the index knew them (a folder: its indexed files); null when not indexed.</summary>
    public long? Size { get; set; }

    /// <summary>Files of a folder, as the index knew them; null for a file or a folder not indexed.</summary>
    public int? Files { get; set; }

    /// <summary>When it was deleted (UTC).</summary>
    public DateTime DeletedAt { get; set; }

    public int? DeletedById { get; set; }

    /// <summary>Who deleted it, as shown then (the account may be gone since).</summary>
    public string? DeletedBy { get; set; }

    /// <summary>Which work folder holds it (the setting may change meanwhile).</summary>
    public WorkFolderLocation Location { get; set; }

    /// <summary>Its own folder under the trash (a GUID): two deletions of the same name never meet.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>The folder rules of a folder and below it, as JSON, given back by a restore (<see cref="Access.FolderRuleService"/>).</summary>
    public string? Rules { get; set; }

    /// <summary>
    /// The default tags of a folder and of the folders below it, as JSON, given back by a restore
    /// (<see cref="Tagging.FolderDefaultTags"/>); null when it had none.
    /// </summary>
    public string? DefaultTags { get; set; }

    public List<TrashTag> Tags { get; set; } = [];

    /// <summary>
    /// Null: something deleted. <see cref="TrashEntryKind.PreviousVersion"/>: a copy of a file's content before it was
    /// saved over (the file itself stays where it is, with its tags and previews).
    /// </summary>
    public TrashEntryKind? Kind { get; set; }
}

public enum TrashEntryKind
{
    /// <summary>A file's content before an editor saved over it (agents/docs/features/text-editor.md).</summary>
    PreviousVersion,
}
