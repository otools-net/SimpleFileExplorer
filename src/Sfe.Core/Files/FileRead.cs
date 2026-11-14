namespace Otools.Net.Sfe.Core.Files;

/// <param name="Bytes">The content; null when the file is larger than asked (<see cref="TooLarge"/>).</param>
/// <param name="Version">Null when the file was too large to be read.</param>
public sealed record FileRead(byte[]? Bytes, FileVersion? Version, long Size, DateTime Modified)
{
    public bool TooLarge => Bytes is null;
}
