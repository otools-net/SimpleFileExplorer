namespace Otools.Net.Sfe.Core.Files;

/// <param name="Target">The folder it goes into (root-relative).</param>
/// <param name="Name">
/// The file's name, or its path below <paramref name="Target"/> for a folder upload (<c>Trip/Day 1/a.jpg</c>): the
/// folders on the way are created when it is finished.
/// </param>
/// <param name="Modified">The file's modification time on the uploader's computer, kept on the uploaded file.</param>
public sealed record UploadStart(string? Target, string? Name, long Size, DateTimeOffset? Modified = null);
