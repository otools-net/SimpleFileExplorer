using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Tagging;

/// <summary>A default tag of a folder taken with it to the trash (<see cref="TrashEntry.DefaultTags"/>).</summary>
/// <param name="Path">The folder it is set on, relative to the folder deleted (empty: that folder itself).</param>
public sealed record KeptDefaultTag(string Path, int TagId, DateTime Created);
