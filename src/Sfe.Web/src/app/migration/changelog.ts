/** A piece of a changelog item: plain text, `code` or **bold** (a link keeps its text only). */
export interface ChangelogPart {
    text: string;
    kind: 'text' | 'code' | 'strong';
}

const INLINE = /`([^`]+)`|\*\*([^*]+)\*\*|\[([^\]]+)\]\([^)]*\)/g;

/** The inline Markdown of a changelog item as parts, drawn as elements (never as HTML). */
export function changelogParts(item: string): ChangelogPart[] {
    const parts: ChangelogPart[] = [];
    let last = 0;
    for (const match of item.matchAll(INLINE)) {
        if (match.index > last)
            parts.push({ text: item.slice(last, match.index), kind: 'text' });
        if (match[1] !== undefined)
            parts.push({ text: match[1], kind: 'code' });
        else if (match[2] !== undefined)
            parts.push({ text: match[2], kind: 'strong' });
        else
            parts.push({ text: match[3], kind: 'text' });
        last = match.index + match[0].length;
    }
    if (last < item.length)
        parts.push({ text: item.slice(last), kind: 'text' });
    return parts;
}
