import { slugify } from '../formats/markdown/markdown';
import { HelpHeading, HelpPage } from './help.models';

/** A page or one of its headings found by the help's search; `section`: the heading's anchor (null: the page). */
export interface HelpMatch {
    page: HelpPage;
    heading: HelpHeading | null;
    section: string | null;
}

/** Lower case, without accents: "Créer" finds "creer". */
function folded(text: string): string {
    return text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
}

function hasAll(text: string, words: string[]): boolean {
    const haystack = folded(text);
    return words.every((word) => haystack.includes(word));
}

/**
 * The pages and headings holding every word of `query` (any case, accents ignored, parts of words too): titles first,
 * then headings with every word, then headings whose page's title gives the rest; in the help's order within each,
 * at most `limit`.
 */
export function searchHelp(pages: readonly HelpPage[], query: string, limit = 40): HelpMatch[] {
    const words = folded(query).split(/\s+/).filter(Boolean);
    if (!words.length) return [];
    const ranked: { rank: number; match: HelpMatch }[] = [];
    for (const page of pages) {
        if (hasAll(page.title, words)) ranked.push({ rank: 0, match: { page, heading: null, section: null } });
        for (const heading of page.headings) {
            const rank = hasAll(heading.text, words)
                ? 1
                : words.some((word) => folded(heading.text).includes(word)) &&
                    hasAll(`${heading.text} ${page.title}`, words)
                  ? 2
                  : -1;
            if (rank > 0) ranked.push({ rank, match: { page, heading, section: slugify(heading.text) } });
        }
    }
    // Array.sort is stable: the help's order stays within a rank.
    return ranked
        .sort((a, b) => a.rank - b.rank)
        .slice(0, limit)
        .map((entry) => entry.match);
}

/** A group of the help's contents: the pages of one folder of the documentation. */
export interface HelpGroup {
    title: string;
    pages: HelpPage[];
}

/** The folders of the documentation, in the order the contents show them, with their titles. */
const GROUPS: readonly { folder: string; title: string }[] = [
    { folder: '', title: $localize`:help contents:Getting started` },
    { folder: 'user-guide', title: $localize`:help contents:Using SFE` },
    { folder: 'administration', title: $localize`:help contents:Administration` },
    { folder: 'reference', title: $localize`:help contents:Reference` },
];

function folderOf(path: string): string {
    const slash = path.lastIndexOf('/');
    return slash < 0 ? '' : path.slice(0, slash);
}

/** The help's pages by folder, for its contents: the known folders first, any other after them under its name. */
export function helpGroups(pages: readonly HelpPage[]): HelpGroup[] {
    const groups = GROUPS.map((group) => ({
        title: group.title,
        pages: pages.filter((page) => folderOf(page.path) === group.folder),
    }));
    const known = new Set(GROUPS.map((group) => group.folder));
    for (const folder of new Set(pages.map((page) => folderOf(page.path)))) {
        if (!known.has(folder))
            groups.push({ title: folder, pages: pages.filter((page) => folderOf(page.path) === folder) });
    }
    return groups.filter((group) => group.pages.length > 0);
}
