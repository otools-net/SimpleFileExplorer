namespace Sfe.Core.Files.Compression;

/// <summary>CRC-32 (IEEE), as zip uses it: for ZipCrypto's keys and checking entries.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = Make();

    /// <summary>One byte into ZipCrypto's keys (the raw step, without the inversions of a checksum).</summary>
    public static uint Update(uint crc, byte b) => Table[(crc ^ b) & 0xFF] ^ (crc >> 8);

    private static uint[] Make()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
