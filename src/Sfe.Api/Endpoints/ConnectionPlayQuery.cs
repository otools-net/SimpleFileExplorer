namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>The query of <c>GET api/connections/{id}/streaming/play</c>.</summary>
/// <param name="T">Seconds from the start of the video.</param>
/// <param name="Player">Names the player asking: its next request stops this stream.</param>
internal sealed record ConnectionPlayQuery(string Path, double T = 0, string? Player = null);
