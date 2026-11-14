namespace Otools.Net.Sfe.Core.Tagging;

/// <summary>The default tags of a folder: its own, and those of its parents, which apply to it too.</summary>
public sealed record FolderDefaults(
    string Path,
    IReadOnlyList<TagSummary> Own,
    IReadOnlyList<InheritedDefaultTag> Inherited
)
{
    public bool IsEmpty => Own.Count == 0 && Inherited.Count == 0;
}
