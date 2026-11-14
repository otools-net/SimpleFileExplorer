namespace Sfe.Core.Connections;

/// <summary>An item chosen for a zip of a connection's files.</summary>
/// <param name="EntryName">Its name in the zip, unique among the chosen ones.</param>
public sealed record RemoteArchiveItem(string Path, string EntryName, bool IsFolder);

/// <summary>The zip of chosen items of a connection, checked at the provider.</summary>
public sealed record RemoteArchivePlan(IReadOnlyList<RemoteArchiveItem> Items, string FileName);

/// <summary>What <see cref="RemoteArchives.PlanAsync"/> answers: a plan, or why there is none.</summary>
/// <param name="Invalid">The choice is refused (none, too many, a path that isn't one); null otherwise.</param>
/// <param name="Missing">An item chosen isn't there.</param>
public sealed record RemoteArchivePlanResult(RemoteArchivePlan? Plan, string? Invalid = null, bool Missing = false);

/// <summary>What a zip of a connection's files held.</summary>
/// <param name="LeftOut">Paths left out (no content, past the limits), listed in the zip too.</param>
public sealed record RemoteArchiveSummary(int Files, long Bytes, IReadOnlyList<string> LeftOut);
