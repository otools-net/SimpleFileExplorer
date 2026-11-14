namespace Otools.Net.Sfe.Core.Files;

public sealed record TrashPurgeStatus(TrashPurgeState State, int Purged, DateTimeOffset? Started);
