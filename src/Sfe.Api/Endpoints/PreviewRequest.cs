using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>The file a preview route asks for (<see cref="ConnectionPreviewFiles.OpenAsync"/>).</summary>
/// <param name="Path">Normalised (<see cref="RemotePaths.Normalize"/>), already checked at the path.</param>
/// <param name="Fresh">Ask the provider rather than the library's index.</param>
internal sealed record PreviewRequest(
    ConnectionFileServices Files,
    ConnectionPreviewServices Previews,
    OpenConnection Open,
    string Path,
    string Right,
    bool Fresh
);
