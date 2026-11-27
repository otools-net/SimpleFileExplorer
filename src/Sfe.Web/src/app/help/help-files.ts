import { encodePath } from '../core/paths';

/**
 * The route of a file of the help (a page or a picture), below `API_BASE`. Apart from help-links.ts: the Api imports
 * it, and help-links.ts brings the Markdown renderer, which stays out of the initial bundle.
 */
export function helpFile(path: string): string {
    return `help/files/${encodePath(path)}`;
}
