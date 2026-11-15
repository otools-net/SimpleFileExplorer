namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>A connection file's row in the library's index, as tagging reads it.</summary>
/// <param name="Path">As the provider spells it (the display path).</param>
/// <param name="ProviderId">Its id at the provider: a rule set on it at another path applies too.</param>
public sealed record RemoteTaggedFile(long Id, string Path, string? ProviderId);
