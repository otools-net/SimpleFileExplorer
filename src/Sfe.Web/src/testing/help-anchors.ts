import { readFileSync } from 'node:fs';
import { slugify } from '../app/formats/markdown/markdown';

/** The anchors of the headings of a page of the repository's docs/ (tests run in src/Sfe.Web). */
export function helpAnchors(page: string): Set<string> {
    const text = readFileSync(`../../docs/${page}`, 'utf8');
    let fenced = false;
    const anchors = new Set<string>();
    for (const line of text.split(/\r?\n/)) {
        if (/^ {0,3}(`{3,}|~{3,})/.test(line)) fenced = !fenced;
        const heading = /^#{1,6}\s+(.+?)\s*#*$/.exec(line);
        if (!fenced && heading) anchors.add(slugify(heading[1].replace(/`|\*\*|\[([^\]]*)\]\([^)]*\)/g, '$1')));
    }
    return anchors;
}
