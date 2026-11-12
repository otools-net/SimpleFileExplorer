namespace Sfe.Core.Files;

/// <summary>A part of a text file (<see cref="FileContent.ReadPartAsync"/>).</summary>
/// <param name="Text">The part as text; null when the file is binary.</param>
/// <param name="Offset">Where the part starts in the file, in bytes (after a cut to a whole line).</param>
/// <param name="End">Where it ends: the offset of the next part.</param>
/// <param name="Size">The file's size when it was read.</param>
/// <param name="Encoding">How it was read (as <see cref="TextCodec"/> names them; a UTF-8 mark is plain
/// <c>utf-8</c>).</param>
public sealed record TextPart(string? Text, long Offset, long End, long Size, string Encoding, DateTime Modified);
