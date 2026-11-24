import { afterEach, describe, expect, it, vi } from 'vitest';
import { chunkDigest } from './chunk-digest';

/** SHA-256 of "hello", base64. */
const HELLO = 'sha-256=:LPJNul+wow4m6DsqxbninhsWHlwfp0JecwQzYpOLmCQ=:';

const bytes = (text: string) => new TextEncoder().encode(text).buffer as ArrayBuffer;

describe('chunkDigest', () => {
    afterEach(() => vi.unstubAllGlobals());

    it('is the SHA-256 of the bytes as the Content-Digest header writes it', async () => {
        expect(await chunkDigest(bytes('hello'))).toBe(HELLO);
    });

    it('is the same without crypto.subtle (a page served over http)', async () => {
        vi.stubGlobal('crypto', {});

        expect(await chunkDigest(bytes('hello'))).toBe(HELLO);
    });
});
