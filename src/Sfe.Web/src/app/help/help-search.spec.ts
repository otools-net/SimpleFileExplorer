import { helpGroups, searchHelp } from './help-search';
import { HelpPage } from './help.models';

const PAGES: HelpPage[] = [
    { path: 'README.md', title: 'SFE documentation', headings: [{ level: 2, text: 'Getting started' }] },
    { path: 'first-start.md', title: 'First start', headings: [{ level: 2, text: 'The setup code' }] },
    {
        path: 'administration/README.md',
        title: 'The administration page',
        headings: [{ level: 2, text: 'Finding a setting' }],
    },
    {
        path: 'user-guide/browsing.md',
        title: 'Browsing',
        headings: [
            { level: 2, text: 'Layouts' },
            { level: 3, text: 'Créer des dossiers' },
        ],
    },
    { path: 'user-guide/search.md', title: 'Search', headings: [{ level: 2, text: 'Browsing results' }] },
    { path: 'extra/notes.md', title: 'Notes', headings: [] },
];

describe('searchHelp', () => {
    it('finds titles first, then headings, in the help order', () => {
        const matches = searchHelp(PAGES, 'brows');

        expect(matches.map((match) => [match.page.path, match.heading?.text ?? null])).toEqual([
            ['user-guide/browsing.md', null],
            ['user-guide/search.md', 'Browsing results'],
        ]);
        expect(matches[1].section).toBe('browsing-results');
    });

    it('needs every word, any case and accents ignored', () => {
        expect(searchHelp(PAGES, 'SETUP code').map((match) => match.heading?.text)).toEqual(['The setup code']);
        expect(searchHelp(PAGES, 'creer').map((match) => match.section)).toEqual(['créer-des-dossiers']);
        expect(searchHelp(PAGES, 'setup layouts')).toEqual([]);
    });

    it("finds a heading whose page's title gives the other words", () => {
        const matches = searchHelp(PAGES, 'layouts browsing');

        expect(matches.map((match) => match.heading?.text)).toEqual(['Layouts']);
    });

    it('finds nothing without words, and at most the limit', () => {
        expect(searchHelp(PAGES, '   ')).toEqual([]);
        expect(searchHelp(PAGES, 'e', 2)).toHaveLength(2);
    });
});

describe('helpGroups', () => {
    it('groups the pages by folder, the known ones in their order, others after them', () => {
        const groups = helpGroups(PAGES);

        expect(groups.map((group) => [group.title, group.pages.map((page) => page.path)])).toEqual([
            ['Getting started', ['README.md', 'first-start.md']],
            ['Using SFE', ['user-guide/browsing.md', 'user-guide/search.md']],
            ['Administration', ['administration/README.md']],
            ['extra', ['extra/notes.md']],
        ]);
    });
});
