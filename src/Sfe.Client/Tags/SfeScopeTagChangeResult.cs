namespace Otools.Net.Sfe.Client;

/// <summary>What adding or removing tags on the files of a scope (<see cref="SfeTagScope"/>) changed.</summary>
public sealed class SfeScopeTagChangeResult
{
    /// <summary>The files of the scope changed (each was given the added tags).</summary>
    public int Files { get; init; }

    /// <summary>How many tags were given to files (a file that had one already doesn't count).</summary>
    public int Added { get; init; }

    /// <summary>How many tags were taken from files.</summary>
    public int Removed { get; init; }
}
