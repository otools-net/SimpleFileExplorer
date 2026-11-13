namespace Sfe.Core.Connections;

/// <summary>What <see cref="IConnectionProvider.TestAsync"/> found: whether the kind's settings work.</summary>
/// <param name="Message">For the administrator, naming no secret.</param>
public sealed record ProviderTest(bool Ok, string Message);
