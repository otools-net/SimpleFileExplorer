namespace Otools.Net.Sfe.Client;

/// <summary>How many files a search of a connection finds, and how much of it the index holds.</summary>
public sealed class SfeConnectionSearchCount
{
    internal SfeConnectionSearchCount(int count, bool capped, SfeIndexCoverage coverage)
    {
        Count = count;
        Capped = capped;
        Coverage = coverage;
    }

    /// <summary>The files counted.</summary>
    public int Count { get; }

    /// <summary>Whether more files match than <see cref="Count"/> (the count stopped at the limit).</summary>
    public bool Capped { get; }

    /// <summary>How much of the connection the index holds (short of complete, files may be missing).</summary>
    public SfeIndexCoverage Coverage { get; }
}
