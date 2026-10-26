// Big archives written as streams (never whole in memory), for measure.mjs: zip (stored, Zip64), tar, and a 7z of
// empty files (a header only: what SharpCompress loads whole). Entries come from a generator of { name, data } (data a
// Buffer, empty for none); a generator function is given where the writer needs two passes (the zip's directory).
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

/** Buffers writes into 4 MB chunks. */
class Output {
    constructor(file) {
        fs.mkdirSync(path.dirname(file), { recursive: true });
        this.fd = fs.openSync(file, 'w');
        this.buffer = Buffer.alloc(4 * 1024 * 1024);
        this.used = 0;
        this.written = 0;
    }

    write(data) {
        if (data.length > this.buffer.length - this.used)
            this.flush();
        if (data.length > this.buffer.length) {
            fs.writeSync(this.fd, data);
        } else {
            data.copy(this.buffer, this.used);
            this.used += data.length;
        }
        this.written += data.length;
    }

    flush() {
        if (this.used > 0)
            fs.writeSync(this.fd, this.buffer, 0, this.used);
        this.used = 0;
    }

    close() {
        this.flush();
        fs.closeSync(this.fd);
    }
}

const DOS_TIME = (18 << 11) | (30 << 5);
const DOS_DATE = ((2025 - 1980) << 9) | (7 << 5) | 14;

/**
 * A zip of stored entries: `entries()` is called twice (the local entries, then the central directory with their
 * offsets). Zip64 end records past 65 534 entries or 4 GB.
 */
export function writeZip(file, entries) {
    const out = new Output(file);
    const offsets = [];
    let count = 0;
    for (const { name, data = Buffer.alloc(0) } of entries()) {
        const nameBytes = Buffer.from(name, 'utf8');
        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(20, 4);
        local.writeUInt16LE(0x0800, 6);
        local.writeUInt16LE(DOS_TIME, 10);
        local.writeUInt16LE(DOS_DATE, 12);
        local.writeUInt32LE(zlib.crc32(data), 14);
        local.writeUInt32LE(data.length, 18);
        local.writeUInt32LE(data.length, 22);
        local.writeUInt16LE(nameBytes.length, 26);
        offsets.push(out.written);
        out.write(local);
        out.write(nameBytes);
        out.write(data);
        count++;
    }
    if (out.written > 0xfffffffe)
        throw new Error('measure.mjs writes zips under 4 GB (offsets in the directory are 32-bit here)');
    const directoryStart = out.written;
    let index = 0;
    for (const { name, data = Buffer.alloc(0) } of entries()) {
        const nameBytes = Buffer.from(name, 'utf8');
        const central = Buffer.alloc(46);
        central.writeUInt32LE(0x02014b50, 0);
        central.writeUInt16LE(20, 4);
        central.writeUInt16LE(20, 6);
        central.writeUInt16LE(0x0800, 8);
        central.writeUInt16LE(DOS_TIME, 12);
        central.writeUInt16LE(DOS_DATE, 14);
        central.writeUInt32LE(zlib.crc32(data), 16);
        central.writeUInt32LE(data.length, 20);
        central.writeUInt32LE(data.length, 24);
        central.writeUInt16LE(nameBytes.length, 28);
        central.writeUInt32LE(name.endsWith('/') ? 0x10 : 0, 38);
        central.writeUInt32LE(offsets[index++], 42);
        out.write(central);
        out.write(nameBytes);
    }
    const directorySize = out.written - directoryStart;
    const zip64 = count > 0xfffe;
    if (zip64) {
        const recordAt = out.written;
        const record = Buffer.alloc(56);
        record.writeUInt32LE(0x06064b50, 0);
        record.writeBigUInt64LE(44n, 4);
        record.writeUInt16LE(45, 12);
        record.writeUInt16LE(45, 14);
        record.writeBigUInt64LE(BigInt(count), 24);
        record.writeBigUInt64LE(BigInt(count), 32);
        record.writeBigUInt64LE(BigInt(directorySize), 40);
        record.writeBigUInt64LE(BigInt(directoryStart), 48);
        out.write(record);
        const locator = Buffer.alloc(20);
        locator.writeUInt32LE(0x07064b50, 0);
        locator.writeBigUInt64LE(BigInt(recordAt), 8);
        locator.writeUInt32LE(1, 16);
        out.write(locator);
    }
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(zip64 ? 0xffff : count, 8);
    end.writeUInt16LE(zip64 ? 0xffff : count, 10);
    end.writeUInt32LE(directorySize, 12);
    end.writeUInt32LE(directoryStart, 16);
    out.write(end);
    out.close();
    return { entries: count, bytes: fs.statSync(file).size };
}

function octal(value, length) {
    return Buffer.from(value.toString(8).padStart(length - 1, '0') + '\0', 'ascii');
}

/** A ustar tar (names up to 100 bytes), its end mark included. */
export function writeTar(file, entries) {
    const out = new Output(file);
    let count = 0;
    for (const { name, data = Buffer.alloc(0) } of entries) {
        const header = Buffer.alloc(512);
        const nameBytes = Buffer.from(name, 'utf8');
        if (nameBytes.length > 100)
            throw new Error(`tar names up to 100 bytes here: ${name}`);
        nameBytes.copy(header, 0);
        octal(0o644, 8).copy(header, 100);
        octal(0, 8).copy(header, 108);
        octal(0, 8).copy(header, 116);
        octal(data.length, 12).copy(header, 124);
        octal(Math.floor(Date.UTC(2025, 6, 14, 18, 30) / 1000), 12).copy(header, 136);
        header.fill(0x20, 148, 156);
        header[156] = 0x30;
        Buffer.from('ustar\u000000', 'ascii').copy(header, 257);
        let sum = 0;
        for (const byte of header)
            sum += byte;
        Buffer.from(sum.toString(8).padStart(6, '0') + '\0 ', 'ascii').copy(header, 148);
        out.write(header);
        if (data.length > 0) {
            out.write(data);
            const pad = (512 - (data.length % 512)) % 512;
            if (pad)
                out.write(Buffer.alloc(pad));
        }
        count++;
    }
    out.write(Buffer.alloc(1024));
    out.close();
    return { entries: count, bytes: fs.statSync(file).size };
}

/** 7z's variable-length number (7-Zip's WriteNumber). */
function number7z(value) {
    let v = BigInt(value);
    let first = 0;
    let mask = 0x80;
    let i = 0;
    for (; i < 8; i++) {
        if (v < (1n << BigInt(7 * (i + 1)))) {
            first |= Number(v >> BigInt(8 * i));
            break;
        }
        first |= mask;
        mask >>= 1;
    }
    const bytes = [first];
    for (; i > 0; i--) {
        bytes.push(Number(v & 0xffn));
        v >>= 8n;
    }
    return Buffer.from(bytes);
}

/** A 7z of empty files only (names: a generator), no packed stream: its header is the whole archive, as millions of entries' list is. */
export function write7z(file, names) {
    const nameParts = [Buffer.from([0])];
    let count = 0;
    for (const name of names) {
        nameParts.push(Buffer.from(name + '\0', 'utf16le'));
        count++;
    }
    const allSet = Buffer.alloc(Math.ceil(count / 8), 0xff);
    if (count % 8)
        allSet[allSet.length - 1] = (0xff << (8 - (count % 8))) & 0xff;
    const property = (id, data) => [Buffer.from([id]), number7z(data.length), data];
    const nameData = Buffer.concat(nameParts);
    const header = Buffer.concat([
        Buffer.from([0x01, 0x05]), number7z(count),
        ...property(0x0e, allSet),
        ...property(0x0f, allSet),
        Buffer.from([0x11]), number7z(nameData.length), nameData,
        Buffer.from([0x00, 0x00]),
    ]);
    const start = Buffer.alloc(20);
    start.writeBigUInt64LE(0n, 0);
    start.writeBigUInt64LE(BigInt(header.length), 8);
    start.writeUInt32LE(zlib.crc32(header), 16);
    const signature = Buffer.alloc(12);
    Buffer.from([0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c, 0, 4]).copy(signature, 0);
    signature.writeUInt32LE(zlib.crc32(start), 8);
    const out = new Output(file);
    out.write(signature);
    out.write(start);
    out.write(header);
    out.close();
    return { entries: count, bytes: fs.statSync(file).size };
}
