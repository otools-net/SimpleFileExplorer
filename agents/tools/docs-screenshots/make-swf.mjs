// A small Flash movie (SWF) for the docs' screenshots and the Flash viewer's checks, written by hand: uncompressed
// (`FWS`), one frame, a background colour, filled rectangles, and optionally ActionScript 1 `getURL` actions (a
// `getURL("b.swf", "_level1")` is `loadMovieNum("b.swf", 1)`: it loads a sibling movie on top).

/** Bits written most significant first, as SWF packs its records. */
class Bits {
    bytes = [];
    current = 0;
    used = 0;

    unsigned(value, count) {
        for (let bit = count - 1; bit >= 0; bit--) {
            this.current = (this.current << 1) | ((value >>> bit) & 1);
            if (++this.used === 8)
                this.flush();
        }
    }

    signed(value, count) {
        this.unsigned(value < 0 ? value + 2 ** count : value, count);
    }

    flush() {
        if (this.used === 0)
            return;
        this.bytes.push((this.current << (8 - this.used)) & 0xff);
        this.current = 0;
        this.used = 0;
    }

    done() {
        this.flush();
        return Buffer.from(this.bytes);
    }
}

/** Bits needed for these signed values. */
function signedBits(...values) {
    let bits = 1;
    for (const value of values)
        while (value < -(2 ** (bits - 1)) || value >= 2 ** (bits - 1))
            bits++;
    return bits;
}

/** A RECT record, in twips (1/20 px). */
function rect(bits, xMin, xMax, yMin, yMax) {
    const count = signedBits(xMin, xMax, yMin, yMax);
    bits.unsigned(count, 5);
    for (const value of [xMin, xMax, yMin, yMax])
        bits.signed(value, count);
}

function tag(code, body = Buffer.alloc(0)) {
    if (body.length < 63) {
        const header = Buffer.alloc(2);
        header.writeUInt16LE((code << 6) | body.length);
        return Buffer.concat([header, body]);
    }
    const header = Buffer.alloc(6);
    header.writeUInt16LE((code << 6) | 0x3f);
    header.writeUInt32LE(body.length, 2);
    return Buffer.concat([header, body]);
}

const rgb = color => Buffer.from([(color >> 16) & 0xff, (color >> 8) & 0xff, color & 0xff]);

/** DefineShape: a rectangle filled with one colour (pixels). */
function rectangleShape(id, { x, y, width, height, color }) {
    const [left, top, w, h] = [x, y, width, height].map(value => Math.round(value * 20));
    const bits = new Bits();
    rect(bits, left, left + w, top, top + h);
    bits.flush();
    // One solid fill, no line style; 1 fill bit, 0 line bits.
    bits.unsigned(1, 8);
    bits.unsigned(0x00, 8);
    for (const byte of rgb(color))
        bits.unsigned(byte, 8);
    bits.unsigned(0, 8);
    bits.unsigned(1, 4);
    bits.unsigned(0, 4);
    // Style change (flags: new styles, line style, fill style 1, fill style 0, move to): move to the top left corner,
    // fill style 1 (the right of the edges, clockwise on screen: the inside).
    const moveBits = signedBits(left, top);
    bits.unsigned(0, 1);
    bits.unsigned(0b00101, 5);
    bits.unsigned(moveBits, 5);
    bits.signed(left, moveBits);
    bits.signed(top, moveBits);
    bits.unsigned(1, 1);
    // Four straight edges (general lines).
    for (const [dx, dy] of [[w, 0], [0, h], [-w, 0], [0, -h]]) {
        const count = Math.max(2, signedBits(dx, dy));
        bits.unsigned(1, 1);
        bits.unsigned(1, 1);
        bits.unsigned(count - 2, 4);
        bits.unsigned(1, 1);
        bits.signed(dx, count);
        bits.signed(dy, count);
    }
    bits.unsigned(0, 6);
    const head = Buffer.alloc(2);
    head.writeUInt16LE(id);
    return tag(2, Buffer.concat([head, bits.done()]));
}

/** PlaceObject2 of a character at a depth. */
function place(id, depth) {
    const body = Buffer.alloc(5);
    body.writeUInt8(0x02, 0);
    body.writeUInt16LE(depth, 1);
    body.writeUInt16LE(id, 3);
    return tag(26, body);
}

/** DoAction with `getURL(url, target)` actions. */
function getUrls(calls) {
    const actions = calls.map(([url, target]) => {
        const strings = Buffer.concat([Buffer.from(url + '\0', 'utf8'), Buffer.from(target + '\0', 'utf8')]);
        const head = Buffer.alloc(3);
        head.writeUInt8(0x83, 0);
        head.writeUInt16LE(strings.length, 1);
        return Buffer.concat([head, strings]);
    });
    return tag(12, Buffer.concat([...actions, Buffer.from([0])]));
}

/**
 * The SWF bytes of a movie of `width` × `height` px on `background`, showing `rectangles` ({ x, y, width, height,
 * color }) and running `getUrl` ([[url, target]]) once.
 */
export function makeSwf({ width = 550, height = 400, background = 0xffffff, rectangles = [], getUrl = [], version = 6, frameRate = 24 } = {}) {
    const header = new Bits();
    rect(header, 0, width * 20, 0, height * 20);
    const frame = Buffer.alloc(4);
    frame.writeUInt16LE(Math.round(frameRate * 256), 0);
    frame.writeUInt16LE(1, 2);
    const body = Buffer.concat([
        header.done(),
        frame,
        tag(9, rgb(background)),
        ...rectangles.flatMap((rectangle, index) => [rectangleShape(index + 1, rectangle), place(index + 1, index + 1)]),
        ...(getUrl.length ? [getUrls(getUrl)] : []),
        tag(1),
        tag(0),
    ]);
    const start = Buffer.alloc(8);
    start.write('FWS', 0, 'ascii');
    start.writeUInt8(version, 3);
    start.writeUInt32LE(8 + body.length, 4);
    return Buffer.concat([start, body]);
}
