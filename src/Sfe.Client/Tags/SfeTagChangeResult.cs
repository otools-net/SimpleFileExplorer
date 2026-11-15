using System.Collections.Generic;

namespace Otools.Net.Sfe.Client;

/// <summary>What adding or removing tags on files changed.</summary>
public sealed class SfeTagChangeResult
{
    internal SfeTagChangeResult(int added, int removed, IReadOnlyList<SfeFileTags> files)
    {
        Added = added;
        Removed = removed;
        Files = files;
    }

    /// <summary>How many tags were given to files (a file that had one already doesn't count).</summary>
    public int Added { get; }

    /// <summary>How many tags were taken from files.</summary>
    public int Removed { get; }

    /// <summary>The files' tags after the change.</summary>
    public IReadOnlyList<SfeFileTags> Files { get; }
}
