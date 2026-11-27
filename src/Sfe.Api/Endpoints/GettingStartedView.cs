using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>The getting-started checklist's items the session may see, in order.</summary>
public sealed record GettingStartedView(IReadOnlyList<GettingStartedItem> Items);
