// A zip of stored entries (no compression), byte for byte the same at each run: for the sample library's archive
// (make-media.mjs) and the archive viewer's check (visual-check/archive-viewer.mjs). UTF-8 names, one fixed date.
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

/** DOS date and time of every entry: 2025-07-14 18:30. */
const DOS_TIME = (18 << 11) | (30 << 5);
const DOS_DATE = ((2025 - 1980) << 9) | (7 << 5) | 14;

/**
 * Writes `entries` (`{ name, data }`, data a Buffer or a string; a name ending with `/` and no data is a folder) as a
 * zip at `file`.
 */
export function writeZip(file, entries) {
    const parts = [];
    const directory = [];
    let offset = 0;
    for (const entry of entries) {
        const name = Buffer.from(entry.name, 'utf8');
        const data = typeof entry.data === 'string' ? Buffer.from(entry.data, 'utf8') : entry.data ?? Buffer.alloc(0);
        const crc = zlib.crc32(data);
        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(20, 4);
        local.writeUInt16LE(0x0800, 6);
        local.writeUInt16LE(DOS_TIME, 10);
        local.writeUInt16LE(DOS_DATE, 12);
        local.writeUInt32LE(crc, 14);
        local.writeUInt32LE(data.length, 18);
        local.writeUInt32LE(data.length, 22);
        local.writeUInt16LE(name.length, 26);
        const central = Buffer.alloc(46);
        central.writeUInt32LE(0x02014b50, 0);
        central.writeUInt16LE(20, 4);
        central.writeUInt16LE(20, 6);
        central.writeUInt16LE(0x0800, 8);
        central.writeUInt16LE(DOS_TIME, 12);
        central.writeUInt16LE(DOS_DATE, 14);
        central.writeUInt32LE(crc, 16);
        central.writeUInt32LE(data.length, 20);
        central.writeUInt32LE(data.length, 24);
        central.writeUInt16LE(name.length, 28);
        central.writeUInt32LE(entry.name.endsWith('/') ? 0x10 : 0, 38);
        central.writeUInt32LE(offset, 42);
        parts.push(local, name, data);
        directory.push(central, name);
        offset += local.length + name.length + data.length;
    }
    const size = directory.reduce((sum, part) => sum + part.length, 0);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(entries.length, 8);
    end.writeUInt16LE(entries.length, 10);
    end.writeUInt32LE(size, 12);
    end.writeUInt32LE(offset, 16);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, Buffer.concat([...parts, ...directory, end]));
}
