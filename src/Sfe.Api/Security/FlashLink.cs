namespace Sfe.Api.Security;

/// <param name="Url">
/// Relative to the app (<c>api/files/flash/{token}/{name}</c>): the movie, whose relative addresses (<c>level2.swf</c>)
/// resolve under the token, to the files of its folder.
/// </param>
public sealed record FlashLink(string Url, DateTimeOffset Expires);
