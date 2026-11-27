import { MarkdownLinks, slugify } from '../formats/markdown/markdown';
import { HELP_HOME, helpFile, helpLink } from './help-files';

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
