namespace Otools.Net.Sfe.Core.Indexing;

/// <summary>What the index holds of a folder, before removing it from the index.</summary>
/// <param name="Indexed">The folder itself is in the index.</param>
/// <param name="Directories">The folder and its sub-folders in the index.</param>
/// <param name="TaggedFiles">Of <paramref name="Files"/>, those with tags.</param>
public sealed record IndexedFolderStats(bool Indexed, int Directories, int Files, int TaggedFiles);
