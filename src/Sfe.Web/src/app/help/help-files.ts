import { encodePath } from '../core/paths';

/*
 * Addresses of the help, apart from help-links.ts: the Api and the wizards import them, and help-links.ts brings the
 * Markdown renderer, which stays out of the initial bundle and out of the chunks that only link to the help.
 */

/** The help's first page (its contents). */
export const HELP_HOME = 'README.md';

/** The route of a file of the help (a page or a picture), below `API_BASE`. */
export function helpFile(path: string): string {
    return `help/files/${encodePath(path)}`;
}

/** The address of a page of the help in the app, at one of its headings: `help?page=…#section`. */
export function helpLink(page: string, section?: string | null): string {
    const address = page === HELP_HOME ? 'help' : `help?page=${encodeURIComponent(page)}`;
    return section ? `${address}#${section}` : address;
}
