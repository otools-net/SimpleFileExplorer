using Sfe.Core.Trickplay;

namespace Sfe.Api.Endpoints;

/// <param name="Paths">Videos to look up, relative to the library root.</param>
public sealed record TrickplayStatesRequest(IReadOnlyList<string>? Paths);

/// <param name="Running">The background job is busy (see the status's state).</param>
/// <param name="Files">States by path, for the videos waiting or being processed only.</param>
public sealed record TrickplayStates(bool Running, IReadOnlyDictionary<string, TrickplayFileState> Files);
