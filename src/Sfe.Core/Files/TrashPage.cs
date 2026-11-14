using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Files;

/// <param name="Next">The <c>before</c> of the next page; null on the last.</param>
/// <param name="RetentionDays">Days things stay in the trash (<see cref="TrashOptions.RetentionDays"/>); 0 = until
/// emptied.</param>
public sealed record TrashPage(IReadOnlyList<TrashItem> Items, bool HasMore, int? Next, int RetentionDays);
