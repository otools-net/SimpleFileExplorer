namespace Otools.Net.Sfe.Core.Files.Compression;

/// <summary>
/// Checks the CRC-32 of what is read at its end: a damaged entry, or a wrong ZipCrypto password (which passes its
/// 1-byte check 1 time in 256). <paramref name="encrypted"/>: a mismatch may be the password.
/// </summary>
internal sealed class CrcCheckStream(Stream inner, uint expected, bool encrypted = true) : ReadOnlyStream(inner)
{
    private readonly System.IO.Hashing.Crc32 _crc = new();

    public override int Read(Span<byte> buffer)
    {
        var read = Inner.Read(buffer);
        _crc.Append(buffer[..read]);
        if (read == 0 && buffer.Length > 0 && _crc.GetCurrentHashAsUInt32() != expected)
        {
            throw encrypted
                ? new ArchiveException(
                    "An entry doesn't match its checksum: the password is wrong or the archive is damaged.",
                    needsPassword: true
                )
                : new ArchiveException("An entry doesn't match its checksum: the archive is damaged.");
        }

        return read;
    }
}
