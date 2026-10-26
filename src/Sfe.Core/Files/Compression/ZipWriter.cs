using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Sfe.Core.Files.Compression;

/// <summary>
/// SFE's own zip writer (agents/docs/features/compression.md): neither .NET's <c>ZipArchive</c> nor SharpCompress
/// writes encrypted zips, nor takes any deflate level. Writes to a seekable stream: each entry's local header gets its
/// CRC and sizes written back after its data (no data descriptors), Zip64 for an entry near 4 GB and for the archive past
/// 65 535 entries or 4 GB, UTF-8 names, the modified time (DOS, and the extended timestamp in UTC), deflate at levels
/// 0–9 (0: stored), and with a password WinZip AES-256 (AE-2: no CRC, which would leak small files' content; a new salt
/// per entry). The central directory goes to a temporary file until the end: memory stays flat whatever the count.
/// </summary>
public sealed class ZipWriter : IDisposable, IAsyncDisposable
{
    private const uint LocalSignature = 0x04034b50;
    private const uint CentralSignature = 0x02014b50;
    private const uint EndSignature = 0x06054b50;
    private const uint End64Signature = 0x06064b50;
    private const uint Locator64Signature = 0x07064b50;
    private const ushort Utf8Flag = 0x0800;
    private const ushort EncryptedFlag = 0x0001;
    private const ushort AesMethod = 99;
    private const int AesStrength = 3;
    private const int BufferSize = 1 << 20;

    /// <summary>An entry this close to 4 GB gets Zip64 sizes (deflate and encryption may add a little).</summary>
    private const long Zip64Threshold = uint.MaxValue - (64L << 20);

    private readonly Stream _output;
    private readonly byte[]? _password;
    private readonly FileStream _directory;
    private readonly byte[] _buffer = new byte[BufferSize];
    private long _entries;
    private bool _finished;

    /// <param name="output">Where the zip goes: seekable and writable, at its start.</param>
    /// <param name="password">Encrypts every file with AES-256 (the names stay readable); null: none.</param>
    public ZipWriter(Stream output, string? password = null)
    {
        if (!output.CanSeek || !output.CanWrite)
            throw new ArgumentException("A zip is written to a seekable stream.", nameof(output));
        _output = output;
        _password = string.IsNullOrEmpty(password) ? null : Encoding.UTF8.GetBytes(password);
        _directory = new FileStream(Path.Combine(Path.GetTempPath(), $"sfe-zip-{Guid.NewGuid():N}.dir"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);
    }

    /// <summary>Entries written so far.</summary>
    public long Entries => _entries;

    /// <summary>Bytes of the zip so far.</summary>
    public long Length => _output.Position;

    /// <summary>A folder entry (<paramref name="name"/> without its trailing <c>/</c>): what keeps an empty folder.</summary>
    public void AddFolder(string name, DateTime modified)
    {
        var entryName = Encoding.UTF8.GetBytes(name.TrimEnd('/') + "/");
        var offset = _output.Position;
        var extra = Extras(zip64: false, modified, aes: false, method: 0);
        WriteLocal(entryName, extra, version: 20, flags: Utf8Flag, method: 0, modified, crc: 0, packed: 0, size: 0, zip64: false);
        AddCentral(entryName, version: 20, flags: Utf8Flag, method: 0, modified, crc: 0, packed: 0, size: 0, offset, folder: true, aes: false);
    }

    /// <summary>
    /// A file entry: <paramref name="input"/> read to its end, deflated at <paramref name="level"/> (0: stored), encrypted
    /// when the writer has a password. <paramref name="read"/> is told the bytes read, as they are.
    /// </summary>
    /// <returns>The bytes the entry's data takes in the zip.</returns>
    public async Task<long> AddFileAsync(string name, DateTime modified, Stream input, int level, Action<int>? read = null, CancellationToken cancel = default)
    {
        if (level is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(level), "A level is 0 (stored) to 9.");
        var entryName = Encoding.UTF8.GetBytes(name);
        var method = (ushort)(level == 0 ? 0 : 8);
        var aes = _password is not null;
        // Known before writing: the local header has room for the Zip64 sizes only when they may be needed.
        var zip64 = !input.CanSeek || input.Length - input.Position >= Zip64Threshold;
        var version = (ushort)(aes ? 51 : zip64 ? 45 : 20);
        var flags = (ushort)(Utf8Flag | (aes ? EncryptedFlag : 0));
        var offset = _output.Position;
        var extra = Extras(zip64, modified, aes, method);
        WriteLocal(entryName, extra, version, flags, aes ? AesMethod : method, modified, crc: 0, packed: 0, size: 0, zip64);
        var dataStart = _output.Position;

        var crc = new System.IO.Hashing.Crc32();
        long size = 0;
        AesCtrWriter? cipher = null;
        Stream sink = new KeptOpen(_output);
        if (aes)
        {
            var salt = RandomNumberGenerator.GetBytes(ZipEncryption.SaltLength(AesStrength));
            var keyLength = ZipEncryption.KeyLength(AesStrength);
            var keys = Rfc2898DeriveBytes.Pbkdf2(_password!, salt, ZipEncryption.AesIterations, HashAlgorithmName.SHA1, keyLength * 2 + ZipEncryption.AesVerifier);
            _output.Write(salt);
            _output.Write(keys.AsSpan(keyLength * 2, ZipEncryption.AesVerifier));
            sink = cipher = new AesCtrWriter(sink, keys[..keyLength], keys[keyLength..(keyLength * 2)]);
        }
        var payloadStart = _output.Position;
        var deflate = method == 8 ? new DeflateStream(sink, new ZLibCompressionOptions { CompressionLevel = level }, leaveOpen: true) : null;
        var top = deflate ?? sink;
        try
        {
            int count;
            while ((count = await input.ReadAsync(_buffer, cancel).ConfigureAwait(false)) > 0)
            {
                crc.Append(_buffer.AsSpan(0, count));
                size += count;
                await top.WriteAsync(_buffer.AsMemory(0, count), cancel).ConfigureAwait(false);
                read?.Invoke(count);
            }
        }
        finally
        {
            // Its last block goes out (the cipher under it stays open for its authentication code).
            if (deflate is not null)
                await deflate.DisposeAsync().ConfigureAwait(false);
        }
        // .NET writes nothing for no data; a deflate stream holds at least its last, empty block (7-Zip says so).
        if (deflate is not null && _output.Position == payloadStart)
            sink.Write([0x03, 0x00]);
        if (cipher is not null)
        {
            _output.Write(cipher.AuthenticationCode());
            cipher.Dispose();
        }
        var end = _output.Position;
        var packed = end - dataStart;
        if (!zip64 && (packed >= uint.MaxValue || size >= uint.MaxValue))
            throw new IOException($"“{name}” grew while it was compressed: try again.");

        // AE-2 keeps no CRC: the authentication code checks the data.
        var stored = aes ? 0u : crc.GetCurrentHashAsUInt32();
        _output.Position = offset + 14;
        Span<byte> sizes = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(sizes, stored);
        BinaryPrimitives.WriteUInt32LittleEndian(sizes[4..], zip64 ? uint.MaxValue : (uint)packed);
        BinaryPrimitives.WriteUInt32LittleEndian(sizes[8..], zip64 ? uint.MaxValue : (uint)size);
        _output.Write(sizes);
        if (zip64)
        {
            // The Zip64 extra is the first: after the name and its 4-byte head.
            _output.Position = offset + 30 + entryName.Length + 4;
            Span<byte> large = stackalloc byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(large, size);
            BinaryPrimitives.WriteInt64LittleEndian(large[8..], packed);
            _output.Write(large);
        }
        _output.Position = end;
        AddCentral(entryName, version, flags, aes ? AesMethod : method, modified, stored, packed, size, offset, folder: false, aes, method);
        return packed;
    }

    /// <summary>Writes the central directory and its end: the zip is complete.</summary>
    public void Finish()
    {
        if (_finished)
            return;
        _finished = true;
        var start = _output.Position;
        _directory.Position = 0;
        _directory.CopyTo(_output);
        var length = _output.Position - start;
        var end = _output.Position;
        var zip64 = _entries >= ushort.MaxValue || start >= uint.MaxValue || length >= uint.MaxValue;
        if (zip64)
        {
            Span<byte> record = stackalloc byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(record, End64Signature);
            BinaryPrimitives.WriteInt64LittleEndian(record[4..], 44);
            BinaryPrimitives.WriteUInt16LittleEndian(record[12..], 45);
            BinaryPrimitives.WriteUInt16LittleEndian(record[14..], 45);
            BinaryPrimitives.WriteInt64LittleEndian(record[24..], _entries);
            BinaryPrimitives.WriteInt64LittleEndian(record[32..], _entries);
            BinaryPrimitives.WriteInt64LittleEndian(record[40..], length);
            BinaryPrimitives.WriteInt64LittleEndian(record[48..], start);
            _output.Write(record);
            Span<byte> locator = stackalloc byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(locator, Locator64Signature);
            BinaryPrimitives.WriteInt64LittleEndian(locator[8..], end);
            BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);
            _output.Write(locator);
        }
        Span<byte> tail = stackalloc byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(tail, EndSignature);
        var count = (ushort)Math.Min(_entries, ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(tail[8..], zip64 ? ushort.MaxValue : count);
        BinaryPrimitives.WriteUInt16LittleEndian(tail[10..], zip64 ? ushort.MaxValue : count);
        BinaryPrimitives.WriteUInt32LittleEndian(tail[12..], zip64 ? uint.MaxValue : (uint)length);
        BinaryPrimitives.WriteUInt32LittleEndian(tail[16..], zip64 ? uint.MaxValue : (uint)start);
        _output.Write(tail);
        _output.Flush();
    }

    private void WriteLocal(byte[] name, byte[] extra, ushort version, ushort flags, ushort method, DateTime modified, uint crc, long packed, long size, bool zip64)
    {
        Span<byte> header = stackalloc byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(header, LocalSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], version);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], method);
        var (time, date) = DosTime(modified);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], time);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], date);
        BinaryPrimitives.WriteUInt32LittleEndian(header[14..], crc);
        BinaryPrimitives.WriteUInt32LittleEndian(header[18..], zip64 ? uint.MaxValue : (uint)packed);
        BinaryPrimitives.WriteUInt32LittleEndian(header[22..], zip64 ? uint.MaxValue : (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], (ushort)extra.Length);
        _output.Write(header);
        _output.Write(name);
        _output.Write(extra);
    }

    private void AddCentral(byte[] name, ushort version, ushort flags, ushort method, DateTime modified, uint crc, long packed, long size, long offset,
        bool folder, bool aes, ushort realMethod = 0)
    {
        var largeSize = size >= uint.MaxValue || packed >= uint.MaxValue;
        var largeOffset = offset >= uint.MaxValue;
        var extra = new List<byte>();
        if (largeSize || largeOffset)
        {
            var fields = new List<byte>();
            if (largeSize)
            {
                fields.AddRange(BitConverter.GetBytes(size));
                fields.AddRange(BitConverter.GetBytes(packed));
            }
            if (largeOffset)
                fields.AddRange(BitConverter.GetBytes(offset));
            extra.AddRange(BitConverter.GetBytes((ushort)0x0001));
            extra.AddRange(BitConverter.GetBytes((ushort)fields.Count));
            extra.AddRange(fields);
        }
        extra.AddRange(Extras(zip64: false, modified, aes, realMethod));
        if (largeSize || largeOffset)
            version = Math.Max(version, (ushort)45);

        Span<byte> record = stackalloc byte[46];
        BinaryPrimitives.WriteUInt32LittleEndian(record, CentralSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(record[4..], 63);   // made by: MS-DOS attributes, spec 6.3
        BinaryPrimitives.WriteUInt16LittleEndian(record[6..], version);
        BinaryPrimitives.WriteUInt16LittleEndian(record[8..], flags);
        BinaryPrimitives.WriteUInt16LittleEndian(record[10..], method);
        var (time, date) = DosTime(modified);
        BinaryPrimitives.WriteUInt16LittleEndian(record[12..], time);
        BinaryPrimitives.WriteUInt16LittleEndian(record[14..], date);
        BinaryPrimitives.WriteUInt32LittleEndian(record[16..], crc);
        BinaryPrimitives.WriteUInt32LittleEndian(record[20..], largeSize ? uint.MaxValue : (uint)packed);
        BinaryPrimitives.WriteUInt32LittleEndian(record[24..], largeSize ? uint.MaxValue : (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(record[28..], (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(record[30..], (ushort)extra.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(record[38..], folder ? 0x10u : 0x20u);
        BinaryPrimitives.WriteUInt32LittleEndian(record[42..], largeOffset ? uint.MaxValue : (uint)offset);
        _directory.Write(record);
        _directory.Write(name);
        _directory.Write(extra.ToArray());
        _entries++;
    }

    /// <summary>The extra fields: Zip64 room (local headers that may need it), the time in UTC, AES' description.</summary>
    private static byte[] Extras(bool zip64, DateTime modified, bool aes, ushort method)
    {
        var extra = new List<byte>(32);
        if (zip64)
        {
            extra.AddRange(BitConverter.GetBytes((ushort)0x0001));
            extra.AddRange(BitConverter.GetBytes((ushort)16));
            extra.AddRange(new byte[16]);
        }
        // Extended timestamp: the modification time, seconds since 1970 in UTC.
        var seconds = (long)Math.Clamp((modified.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds, 0, uint.MaxValue);
        extra.AddRange(BitConverter.GetBytes((ushort)0x5455));
        extra.AddRange(BitConverter.GetBytes((ushort)5));
        extra.Add(1);
        extra.AddRange(BitConverter.GetBytes((uint)seconds));
        if (aes)
        {
            extra.AddRange(BitConverter.GetBytes((ushort)0x9901));
            extra.AddRange(BitConverter.GetBytes((ushort)7));
            extra.AddRange(BitConverter.GetBytes((ushort)2));   // AE-2
            extra.Add((byte)'A');
            extra.Add((byte)'E');
            extra.Add(AesStrength);
            extra.AddRange(BitConverter.GetBytes(method));
        }
        return [.. extra];
    }

    /// <summary>The DOS time and date of a zip (local time, 2-second steps, 1980 to 2107).</summary>
    private static (ushort Time, ushort Date) DosTime(DateTime modified)
    {
        var local = modified.Kind == DateTimeKind.Utc ? modified.ToLocalTime() : modified;
        if (local.Year < 1980)
            local = new DateTime(1980, 1, 1);
        else if (local.Year > 2107)
            local = new DateTime(2107, 12, 31, 23, 59, 58);
        return ((ushort)((local.Hour << 11) | (local.Minute << 5) | (local.Second / 2)), (ushort)(((local.Year - 1980) << 9) | (local.Month << 5) | local.Day));
    }

    public void Dispose() => _directory.Dispose();

    public ValueTask DisposeAsync() => _directory.DisposeAsync();

    /// <summary>The zip under an entry's streams: the entry ends, the zip stays open.</summary>
    private sealed class KeptOpen(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>WinZip AES while writing: AES-CTR (a little-endian counter from 1), HMAC-SHA1 of what it writes.</summary>
    private sealed class AesCtrWriter : Stream
    {
        private readonly Stream _inner;
        private readonly Aes _aes;
        private readonly IncrementalHash _mac;
        private readonly byte[] _counter = new byte[16];
        private readonly byte[] _keystream = new byte[16];
        private byte[] _work = new byte[64 * 1024];
        private int _used = 16;

        public AesCtrWriter(Stream inner, byte[] key, byte[] macKey)
        {
            _inner = inner;
            _aes = System.Security.Cryptography.Aes.Create();
            _aes.Key = key;
            _mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, macKey);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /// <summary>The first 10 bytes of the HMAC of all that was written: what follows the data.</summary>
        public byte[] AuthenticationCode() => _mac.GetHashAndReset()[..ZipEncryption.AesAuthCode];

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_work.Length < buffer.Length)
                _work = new byte[buffer.Length];
            var data = _work.AsSpan(0, buffer.Length);
            for (var i = 0; i < buffer.Length; i++)
            {
                if (_used == 16)
                    Next();
                data[i] = (byte)(buffer[i] ^ _keystream[_used++]);
            }
            _mac.AppendData(data);
            _inner.Write(data);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void Next()
        {
            for (var i = 0; i < 16 && ++_counter[i] == 0; i++)
            {
            }
            _aes.EncryptEcb(_counter, _keystream, PaddingMode.None);
            _used = 0;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

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
