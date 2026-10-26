using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Sfe.Core.Files.Compression;

/// <summary>
/// Decrypting zip entries (agents/docs/development/compressed-archives-plan.md, "Passwords"): ZipCrypto (old archives)
/// and WinZip AES (AE-1, AE-2; 128, 192, 256 bits). Ours, not SharpCompress' zip: our directory reader keeps memory flat
/// whatever the count, and AES is also what SFE writes.
/// </summary>
internal static class ZipEncryption
{
    public const int ZipCryptoHeader = 12;
    public const int AesVerifier = 2;
    public const int AesAuthCode = 10;
    public const int AesIterations = 1000;

    public static int SaltLength(int strength) => strength switch { 1 => 8, 2 => 12, 3 => 16, _ => throw Broken() };

    public static int KeyLength(int strength) => strength switch { 1 => 16, 2 => 24, 3 => 32, _ => throw Broken() };

    /// <summary>
    /// The entry's compressed data, decrypted from <paramref name="packed"/> (its whole packed data): checked against the
    /// password at once (ZipCrypto's check byte, AES' verifier), and AES' authentication code at the end.
    /// </summary>
    public static Stream Decrypt(Stream packed, ArchiveEntry entry, string? password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArchiveException("This entry is protected by a password.", needsPassword: true);
        var secret = Encoding.UTF8.GetBytes(password);
        return entry.Cipher switch
        {
            ZipCipher.ZipCrypto => ZipCrypto(packed, entry, secret),
            ZipCipher.Aes => Aes(packed, entry, secret),
            _ => packed,
        };
    }

    private static Stream ZipCrypto(Stream packed, ArchiveEntry entry, byte[] secret)
    {
        if (entry.Packed < ZipCryptoHeader)
            throw Broken();
        var keys = new ZipCryptoKeys(secret);
        Span<byte> header = stackalloc byte[ZipCryptoHeader];
        packed.ReadExactly(header);
        keys.Decrypt(header);
        if (header[^1] != (byte)entry.CipherInfo)
            throw WrongPassword();
        return new ZipCryptoStream(packed, keys);
    }

    private static Stream Aes(Stream packed, ArchiveEntry entry, byte[] secret)
    {
        var strength = entry.CipherInfo & 0xF;
        var salt = new byte[SaltLength(strength)];
        var keyLength = KeyLength(strength);
        var data = entry.Packed - salt.Length - AesVerifier - AesAuthCode;
        if (data < 0)
            throw Broken();
        packed.ReadExactly(salt);
        Span<byte> verifier = stackalloc byte[AesVerifier];
        packed.ReadExactly(verifier);
        var keys = Rfc2898DeriveBytes.Pbkdf2(secret, salt, AesIterations, HashAlgorithmName.SHA1, keyLength * 2 + AesVerifier);
        if (!CryptographicOperations.FixedTimeEquals(keys.AsSpan(keyLength * 2, AesVerifier), verifier))
            throw WrongPassword();
        return new AesCtrStream(packed, keys[..keyLength], keys[keyLength..(keyLength * 2)], data);
    }

    public static ArchiveException WrongPassword() =>
        new("That password doesn't open this archive.", needsPassword: true);

    private static ArchiveException Broken() => new("An encrypted entry of the zip is damaged.");

    /// <summary>The traditional cipher's three keys, as PKWARE's APPNOTE describes them.</summary>
    internal sealed class ZipCryptoKeys
    {
        private uint _k0 = 0x12345678, _k1 = 0x23456789, _k2 = 0x34567890;

        public ZipCryptoKeys(ReadOnlySpan<byte> password)
        {
            foreach (var b in password)
                Update(b);
        }

        public void Decrypt(Span<byte> data)
        {
            for (var i = 0; i < data.Length; i++)
            {
                var temp = (ushort)(_k2 | 2);
                var plain = (byte)(data[i] ^ (byte)((temp * (temp ^ 1)) >> 8));
                Update(plain);
                data[i] = plain;
            }
        }

        private void Update(byte b)
        {
            _k0 = Crc32.Update(_k0, b);
            _k1 = (_k1 + (_k0 & 0xFF)) * 134775813 + 1;
            _k2 = Crc32.Update(_k2, (byte)(_k1 >> 24));
        }
    }

    private sealed class ZipCryptoStream(Stream inner, ZipCryptoKeys keys) : ReadOnlyStream(inner)
    {
        public override int Read(Span<byte> buffer)
        {
            var read = Inner.Read(buffer);
            keys.Decrypt(buffer[..read]);
            return read;
        }
    }

    /// <summary>
    /// WinZip AES: AES in counter mode (a little-endian counter from 1), HMAC-SHA1 of the encrypted data, whose first 10
    /// bytes follow the data. A mismatch at the end is a wrong password or a damaged entry: the data read is not to be
    /// trusted, and the reader gets an error instead of the end.
    /// </summary>
    private sealed class AesCtrStream : ReadOnlyStream
    {
        private readonly Aes _aes;
        private readonly IncrementalHash _mac;
        private readonly byte[] _counter = new byte[16];
        private readonly byte[] _keystream = new byte[16];
        private int _used = 16;
        private long _left;

        public AesCtrStream(Stream inner, byte[] key, byte[] macKey, long length) : base(inner)
        {
            _aes = System.Security.Cryptography.Aes.Create();
            _aes.Key = key;
            _mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, macKey);
            _left = length;
        }

        public override int Read(Span<byte> buffer)
        {
            if (_left == 0)
                return 0;
            var read = Inner.Read(buffer[..(int)Math.Min(buffer.Length, _left)]);
            if (read == 0)
                throw new ArchiveException("An encrypted entry of the zip is cut off: the archive is broken.");
            var data = buffer[..read];
            _mac.AppendData(data);
            for (var i = 0; i < data.Length; i++)
            {
                if (_used == 16)
                    Next();
                data[i] ^= _keystream[_used++];
            }
            _left -= read;
            if (_left == 0)
                CheckAuthentication();
            return read;
        }

        private void Next()
        {
            for (var i = 0; i < 16 && ++_counter[i] == 0; i++)
            {
            }
            _aes.EncryptEcb(_counter, _keystream, PaddingMode.None);
            _used = 0;
        }

        private void CheckAuthentication()
        {
            Span<byte> code = stackalloc byte[AesAuthCode];
            Inner.ReadExactly(code);
            if (!CryptographicOperations.FixedTimeEquals(_mac.GetHashAndReset().AsSpan(0, AesAuthCode), code))
                throw new ArchiveException("An encrypted entry doesn't match its authentication: the password is wrong or the archive is damaged.",
                    needsPassword: true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _aes.Dispose();
                _mac.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

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

/// <summary>A stream that only reads, over another it owns.</summary>
internal abstract class ReadOnlyStream(Stream inner) : Stream
{
    protected Stream Inner { get; } = inner;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public abstract override int Read(Span<byte> buffer);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Checks the CRC-32 of what is read at its end: a damaged entry, or a wrong ZipCrypto password (which passes its 1-byte
/// check 1 time in 256). <paramref name="encrypted"/>: a mismatch may be the password.
/// </summary>
internal sealed class CrcCheckStream(Stream inner, uint expected, bool encrypted = true) : ReadOnlyStream(inner)
{
    private readonly System.IO.Hashing.Crc32 _crc = new();

    public override int Read(Span<byte> buffer)
    {
        var read = Inner.Read(buffer);
        _crc.Append(buffer[..read]);
        if (read == 0 && buffer.Length > 0 && _crc.GetCurrentHashAsUInt32() != expected)
            throw encrypted
                ? new ArchiveException("An entry doesn't match its checksum: the password is wrong or the archive is damaged.", needsPassword: true)
                : new ArchiveException("An entry doesn't match its checksum: the archive is damaged.");
        return read;
    }
}
