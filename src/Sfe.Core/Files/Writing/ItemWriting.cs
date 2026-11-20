namespace Otools.Net.Sfe.Core.Files;

/// <summary>A file or folder being written, as open listings are told it (the change feed's `writing` event).</summary>
/// <param name="Name">Its name in the folder while written (<c>name.sfe-partial</c>).</param>
/// <param name="FinalName">The name it takes when done.</param>
/// <param name="BytesDone">The bytes written so far (an extraction: read from the archive).</param>
/// <param name="BytesTotal">The bytes it will have; 0 when unknown.</param>
public sealed record ItemWriting(string Name, string FinalName, long BytesDone, long BytesTotal);
