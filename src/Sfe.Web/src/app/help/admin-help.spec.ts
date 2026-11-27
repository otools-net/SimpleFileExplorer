import { readFileSync } from 'node:fs';
import { ADMIN_SECTIONS } from '../admin/admin-sections';
import { slugify } from '../formats/markdown/markdown';
import { ADMIN_HELP, adminHelpLink } from './admin-help';

/** The anchors of the headings of a page of the repository's docs/ (tests run in src/Sfe.Web). */
function anchorsOf(page: string): Set<string> {
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

describe('ADMIN_HELP', () => {
    it('has a page for every section of the administration page and a heading for every sub-page it names', () => {
        for (const section of ADMIN_SECTIONS) {
            const help = ADMIN_HELP[section.id];
            expect(help, section.id).toBeDefined();
            const anchors = anchorsOf(help.page);
            for (const subpage of section.subpages) {
                const anchor = help.subpages?.[subpage.id];
                if (section.subpages.length > 1) expect(anchor, `${section.id} › ${subpage.id}`).toBeDefined();
                if (anchor) expect(anchors.has(anchor), `${help.page}#${anchor}`).toBe(true);
            }
        }
    });

    it('names no section the administration page lacks', () => {
        const ids = new Set(ADMIN_SECTIONS.map((section) => section.id));

        expect(Object.keys(ADMIN_HELP).filter((id) => !ids.has(id))).toEqual([]);
    });

    it("links to the sub-page's heading, or to the page", () => {
        expect(adminHelpLink('tags', 'list')).toBe('help?page=administration%2Ftags.md#all-tags');
        expect(adminHelpLink('thumbnails', 'settings')).toBe('help?page=administration%2Fthumbnails.md');
        expect(adminHelpLink('nothing', 'x')).toBeNull();
    });
});
