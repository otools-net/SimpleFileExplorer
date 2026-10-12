/* Library paths: root-relative, segments separated by '/', '' for the root. */

/** Encodes each segment of a library path so names with '#', '?', '%' or '+' stay valid in a URL. */
export function encodePath(path: string): string {
    return path.split('/').map(encodeURIComponent).join('/');
}

export function parentPath(path: string): string {
    return path.split('/').slice(0, -1).join('/');
}

/** The last segment: the name of the file or folder ('' for the root). */
export function baseName(path: string): string {
    return path.split('/').pop() ?? '';
}
