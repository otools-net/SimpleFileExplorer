namespace Sfe.Api.Endpoints;

/// <param name="Path">The movie (<c>.swf</c>), relative to the library root.</param>
public sealed record FlashLinkRequest(string? Path);
