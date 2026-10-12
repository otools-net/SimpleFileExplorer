import { TagDetails, TagSummary } from '../core/models';

/*
 * Tags follow TagStudio's model: a name, a shorthand, aliases, parent tags (a file tagged "Shrek" is found by
 * searching for its parent "Character"), a color, and the "category" and "hidden" properties.
 */

/** Search query finding the files with a tag (and the tags inheriting from it). */
export function tagQuery(tag: Pick<TagSummary, 'name'>): string {
    return `t:"${tag.name.replaceAll('"', '')}"`;
}

/** Text color readable on a tag color (WCAG relative luminance). */
export function textOn(hex: string): string {
    const channel = (value: number) => {
        const c = value / 255;
        return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    };
    const [r, g, b] = [1, 3, 5].map(i => channel(parseInt(hex.slice(i, i + 2), 16)));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.32 ? '#16181d' : '#ffffff';
}

/** Shorthand, aliases and parents of a tag, shown next to it in suggestion lists. */
export function describeTag(tag: TagDetails): string {
    const parts: string[] = [];
    if (tag.shorthand)
        parts.push(tag.shorthand);
    if (tag.aliases.length)
        parts.push(tag.aliases.join(', '));
    if (tag.parents.length)
        parts.push(`in ${tag.parents.map(p => p.displayName).join(', ')}`);
    return parts.join(' · ');
}

/** A term of the search box naming a tag: `t:`, `tag:`, `-t:`... */
export const TAG_TERM = /^(-?)(t:|tag:)(.*)$/is;

/**
 * Bounds of the search term around `caret`. Terms are separated by spaces and commas outside double quotes, as on
 * the server (SearchQuery.SplitOutsideQuotes).
 */
export function termAt(text: string, caret: number): { start: number; end: number } {
    let start = 0;
    let quoted = false;
    for (let i = 0; i < caret; i++) {
        if (text[i] === '"')
            quoted = !quoted;
        else if (!quoted && (text[i] === ' ' || text[i] === ','))
            start = i + 1;
    }
    let end = caret;
    for (; end < text.length; end++) {
        if (text[end] === '"')
            quoted = !quoted;
        else if (!quoted && (text[end] === ' ' || text[end] === ','))
            break;
    }
    return { start, end };
}

/** A tag name as a search term value: quoted when it holds a separator. */
export function termValue(name: string): string {
    return /[\s,"]/.test(name) ? `"${name.replaceAll('"', '')}"` : name;
}
