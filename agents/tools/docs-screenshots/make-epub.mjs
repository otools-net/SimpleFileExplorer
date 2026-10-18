// A small EPUB for the docs' screenshots, written by hand: a zip without compression (as `mimetype` must be anyway),
// with its container, package, table of contents, a style and a few chapters. Each chapter: { title, paragraphs }.

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++)
        c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    return c >>> 0;
});

function crc32(bytes) {
    let crc = 0xffffffff;
    for (const byte of bytes)
        crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
    return (crc ^ 0xffffffff) >>> 0;
}

/** A zip of `files` ([name, text]), stored: local headers, then the central directory. */
function zip(files) {
    const parts = [];
    const central = [];
    let offset = 0;
    for (const [name, text] of files) {
        const data = Buffer.from(text, 'utf8');
        const nameBytes = Buffer.from(name, 'utf8');
        const crc = crc32(data);
        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(20, 4);
        local.writeUInt16LE(0x0800, 6);   // UTF-8 names
        local.writeUInt16LE(0, 8);        // stored
        local.writeUInt32LE(crc, 14);
        local.writeUInt32LE(data.length, 18);
        local.writeUInt32LE(data.length, 22);
        local.writeUInt16LE(nameBytes.length, 26);
        parts.push(local, nameBytes, data);
        const entry = Buffer.alloc(46);
        entry.writeUInt32LE(0x02014b50, 0);
        entry.writeUInt16LE(20, 4);
        entry.writeUInt16LE(20, 6);
        entry.writeUInt16LE(0x0800, 8);
        entry.writeUInt32LE(crc, 16);
        entry.writeUInt32LE(data.length, 20);
        entry.writeUInt32LE(data.length, 24);
        entry.writeUInt16LE(nameBytes.length, 28);
        entry.writeUInt32LE(offset, 42);
        central.push(entry, nameBytes);
        offset += local.length + nameBytes.length + data.length;
    }
    const directory = Buffer.concat(central);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(files.length, 8);
    end.writeUInt16LE(files.length, 10);
    end.writeUInt32LE(directory.length, 12);
    end.writeUInt32LE(offset, 16);
    return Buffer.concat([...parts, directory, end]);
}

const escape = text => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** The EPUB bytes of a book titled `title` by `author`, with `chapters`. */
export function makeEpub(title, author, chapters) {
    const ids = chapters.map((_, index) => `chapter-${index + 1}`);
    const files = [
        ['mimetype', 'application/epub+zip'],
        ['META-INF/container.xml', '<?xml version="1.0"?>\n<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>'],
        ['OEBPS/content.opf', `<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
<metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">sfe-docs-${title.toLowerCase().replace(/\W+/g, '-')}</dc:identifier><dc:title>${escape(title)}</dc:title><dc:creator>${escape(author)}</dc:creator><dc:language>en</dc:language><meta property="dcterms:modified">2025-07-01T00:00:00Z</meta></metadata>
<manifest><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/><item id="style" href="style.css" media-type="text/css"/>${ids.map(id => `<item id="${id}" href="${id}.xhtml" media-type="application/xhtml+xml"/>`).join('')}</manifest>
<spine>${ids.map(id => `<itemref idref="${id}"/>`).join('')}</spine>
</package>`],
        ['OEBPS/style.css', 'body { margin: 0; }\nh1 { margin: 0 0 1em; font-size: 1.6em; }\np { margin: 0; text-indent: 1.5em; text-align: justify; }\nh1 + p { text-indent: 0; }\n'],
        ['OEBPS/nav.xhtml', `<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Contents</title></head><body><nav epub:type="toc"><ol>${chapters.map((chapter, index) => `<li><a href="${ids[index]}.xhtml">${escape(chapter.title)}</a></li>`).join('')}</ol></nav></body></html>`],
        ...chapters.map((chapter, index) => [`OEBPS/${ids[index]}.xhtml`, `<?xml version="1.0" encoding="UTF-8"?>
<html xmlns="http://www.w3.org/1999/xhtml"><head><title>${escape(chapter.title)}</title><link rel="stylesheet" href="style.css"/></head><body><h1>${escape(chapter.title)}</h1>${chapter.paragraphs.map(paragraph => `<p>${escape(paragraph)}</p>`).join('')}</body></html>`]),
    ];
    return zip(files);
}
