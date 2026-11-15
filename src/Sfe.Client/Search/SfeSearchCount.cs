namespace Otools.Net.Sfe.Client;

/// <summary>How many files a search finds, counted up to the server's limit (10,000).</summary>
public sealed class SfeSearchCount
{
    internal SfeSearchCount(int count, bool capped)
    {
        Count = count;
        Capped = capped;
    }

    /// <summary>The files counted.</summary>
    public int Count { get; }

    /// <summary>Whether more files match than <see cref="Count"/> (the count stopped at the limit).</summary>
    public bool Capped { get; }
}
