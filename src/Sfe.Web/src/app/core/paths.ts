/* Library paths: root-relative, segments separated by '/', '' for the root. */

/** Encodes each segment of a library path so names with '#', '?', '%' or '+' stay valid in a URL. */
export function encodePath(path: string): string {
    return path.split('/').map(encodeURIComponent).join('/');
}

export function parentPath(path: string): string {
    return path.split('/').slice(0, -1).join('/');
}

/**
 * A path as one URL segment (base64url of its UTF-8), as the server reads it (`EpubEntries.Token`): the files inside a
 * book or an archive are served below it, and links inside a book resolve under it.
 */
export function pathToken(path: string): string {
    let binary = '';
    for (const byte of new TextEncoder().encode(path))
        binary += String.fromCharCode(byte);
    return btoa(binary).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
}

/** The last segment: the name of the file or folder ('' for the root). */
export function baseName(path: string): string {
    return path.split('/').pop() ?? '';
}
