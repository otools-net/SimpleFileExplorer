import { MarkdownLinks, slugify } from '../formats/markdown/markdown';
import { helpFile } from './help-files';

/** The help's first page (its contents). */
export const HELP_HOME = 'README.md';

/** The address of a page of the help in the app, at one of its headings: `help?page=…#section`. */
export function helpLink(page: string, section?: string | null): string {
    const address = page === HELP_HOME ? 'help' : `help?page=${encodeURIComponent(page)}`;
    return section ? `${address}#${section}` : address;
}

/**
 * Where the links and pictures of a help page lead: other pages to the help page, pictures to the help's files
 * (`apiBase` + the route); links leaving the help (`../AGENTS.md`) lead nowhere, as `renderMarkdown` does.
 */
export function helpMarkdownLinks(apiBase: string): MarkdownLinks {
    return {
        file: (path) => apiBase + helpFile(path),
        view: (path, fragment) => helpLink(path, fragment ? slugify(fragment) : null),
        folder: () => helpLink(HELP_HOME),
        viewable: (path) => /\.md$/i.test(path),
    };
}
