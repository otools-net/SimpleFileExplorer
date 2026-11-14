namespace Otools.Net.Sfe.Core.Files;

/// <param name="Bytes">The encoded text; null when some characters can't be written in the encoding.</param>
/// <param name="Unrepresentable">Those characters (at most ten), when <paramref name="Bytes"/> is null.</param>
public sealed record EncodedText(byte[]? Bytes, IReadOnlyList<string> Unrepresentable);
