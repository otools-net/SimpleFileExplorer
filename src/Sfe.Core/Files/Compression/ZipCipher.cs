namespace Otools.Net.Sfe.Core.Files.Compression;

/// <summary>
/// How a zip entry is encrypted (other formats: SharpCompress knows, <see cref="ArchiveEntry.Encrypted"/> says).
/// </summary>
public enum ZipCipher
{
    None = 0,

    /// <summary>The traditional PKWARE cipher: read (old archives), never written.</summary>
    ZipCrypto = 1,

    /// <summary>WinZip AES (method 99, extra field <c>0x9901</c>).</summary>
    Aes = 2,
}
