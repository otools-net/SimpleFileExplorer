namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// What the page before told about the next one: its last row (the next page reads after it) and the counts read
/// with the first page.
/// </summary>
internal sealed record IndexListingPlace(IndexListingRow Last, IndexListingCounts Counts, DateTimeOffset Kept);
