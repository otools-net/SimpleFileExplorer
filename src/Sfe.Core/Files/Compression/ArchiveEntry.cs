namespace Sfe.Core.Files.Compression;

public enum ArchiveEntryKind
{
    File = 0,
    Folder = 1,
    /// <summary>A symbolic or hard link: listed, never created on extraction.</summary>
    Link = 2,
    /// <summary>A device, a FIFO, a socket: listed, never created.</summary>
    Other = 3,
}

/// <summary>How a zip entry is encrypted (other formats: SharpCompress knows, <see cref="ArchiveEntry.Encrypted"/> says).</summary>
public enum ZipCipher
{
    None = 0,
    /// <summary>The traditional PKWARE cipher: read (old archives), never written.</summary>
    ZipCrypto = 1,
    /// <summary>WinZip AES (method 99, extra field <c>0x9901</c>).</summary>
    Aes = 2,
}

/// <summary>
/// One entry as an archive's directory lists it, before its name is checked (<see cref="ArchiveEntryNames"/>).
/// </summary>
/// <param name="Name">The name as written in the archive (any separator, maybe hostile).</param>
/// <param name="Time">The modified time as stored (zip: local time of the machine that made it).</param>
/// <param name="Locator">Where the entry is: zip, the offset of its local header; other formats, its position in
/// the archive's order.</param>
/// <param name="Method">Zip's compression method (0 stored, 8 deflate…; for AES, the method under the encryption); 0
/// for other formats.</param>
/// <param name="Crc">Zip's CRC-32 of the data (0 when AE-2 leaves it out).</param>
/// <param name="CipherInfo">ZipCrypto: the check byte; AES: the strength (1–3, 128–256 bits) plus 16 × the vendor
/// version (1: AE-1, with a CRC; 2: AE-2, without).</param>
public sealed record ArchiveEntry(
    string Name,
    ArchiveEntryKind Kind,
    long Size,
    long Packed,
    DateTime? Time,
    bool Encrypted,
    long Locator,
    int Method = 0,
    uint Crc = 0,
    ZipCipher Cipher = ZipCipher.None,
    int CipherInfo = 0);
