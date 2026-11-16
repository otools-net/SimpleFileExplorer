namespace Otools.Net.Sfe.Core.Connections;

/// <summary>A small picture of a file made by its provider, with what the provider told of the file then.</summary>
/// <param name="Media">Size and length of the file itself (Dropbox's <c>media_info</c>); null when not told.</param>
public sealed record RemoteThumbnail(byte[] Bytes, string ContentType, RemoteMedia? Media = null);
