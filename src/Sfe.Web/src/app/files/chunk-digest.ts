import { ApiError } from '../core/api-error';
import { UploadChunk } from '../core/api-query';

/**
 * The `Content-Digest` of an upload's chunk (RFC 9530: `sha-256=:<base64>:`), which the server checks before the
 * bytes count, so that bytes damaged on the way are sent again instead of landing in the file. `crypto.subtle` only
 * exists on a secure page (https, or this computer): elsewhere (http on a home network) `@noble/hashes`, loaded the
 * first time it is needed.
 */
export async function chunkDigest(bytes: ArrayBuffer): Promise<string> {
    return `sha-256=:${toBase64(await sha256(bytes))}:`;
}

async function sha256(bytes: ArrayBuffer): Promise<Uint8Array> {
    const subtle = globalThis.crypto?.subtle;
    if (subtle) return new Uint8Array(await subtle.digest('SHA-256', bytes));
    const { sha256: noble } = await import('@noble/hashes/sha2.js');
    return noble(new Uint8Array(bytes));
}

function toBase64(bytes: Uint8Array): string {
    let text = '';
    for (const byte of bytes) text += String.fromCharCode(byte);
    return btoa(text);
}

/** The bytes of `file` from `start`, read once (those hashed are those sent), and their digest. */
export async function readChunk(file: Blob, start: number, size: number): Promise<UploadChunk> {
    const bytes = await file.slice(start, start + size).arrayBuffer();
    return { bytes: new Blob([bytes]), digest: await chunkDigest(bytes) };
}

/** A chunk refused for good (400), not one found damaged on the way (`chunk-digest`: sent again, as a try). */
export const isRefusedChunk = (error: unknown) =>
    error instanceof ApiError && error.status === 400 && error.type !== 'chunk-digest';
