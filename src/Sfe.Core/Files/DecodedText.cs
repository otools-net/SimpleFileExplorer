namespace Sfe.Core.Files;

/// <param name="Text">The content, line endings as they are in the file.</param>
/// <param name="Encoding">How the bytes were read: <see cref="TextCodec.Utf8"/>, <see cref="TextCodec.Utf8Bom"/>,
/// <see cref="TextCodec.Utf16Le"/>, <see cref="TextCodec.Utf16Be"/> or <see cref="TextCodec.Windows1252"/>.</param>
/// <param name="LineEnding"><c>lf</c>, <c>crlf</c>, <c>cr</c>, or <c>mixed</c> when several are used (<c>lf</c> without
/// any).</param>
public sealed record DecodedText(string Text, string Encoding, string LineEnding);
