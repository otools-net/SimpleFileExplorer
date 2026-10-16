import { markWords, searchAdmin } from './admin-search';
import { ADMIN_SECTIONS } from './admin-sections';
import { TABS } from '../settings/settings-schema';

const titles = (query: string) => searchAdmin(query, ADMIN_SECTIONS).map(result => result.title);

describe('searchAdmin', () => {
    it('finds nothing without a word', () => {
        expect(searchAdmin('  ', ADMIN_SECTIONS)).toEqual([]);
    });

    it('finds every setting of the settings pages, where it is', () => {
        const keys = TABS.flatMap(tab => tab.groups.flatMap(group => group.fields.map(field => field.key)));
        for (const key of keys)
            expect(searchAdmin(key, ADMIN_SECTIONS).some(result => result.setting === key)).toBe(true);

        const [result] = searchAdmin('Pictures per folder', ADMIN_SECTIONS);
        expect(result).toMatchObject({ setting: 'browse.folderPictures', where: 'Interface › Browsing › Listings' });
        expect(result.section.id).toBe('interface');
        expect(result.subpage.id).toBe('browsing');

        expect(searchAdmin('streaming.quality', ADMIN_SECTIONS)[0]).toMatchObject({ where: 'Playback › Streaming › Videos converted by the server' });
        expect(searchAdmin('video streams', ADMIN_SECTIONS)[0]).toMatchObject({ title: 'Video streams', where: 'Playback' });
    });

    it('needs every word, in any case and with or without accents, and lists the titles holding them first', () => {
        // In descriptions only ("4 makes a 2×2 grid"), then in a title.
        expect(titles('2×2 GRID')).toEqual(['Pictures per folder']);
        expect(titles('pïctures fôlder')[0]).toBe('Pictures per folder');
        const found = titles('trickplay');
        expect(found[0]).toBe('Trickplay');
        expect(found.indexOf('Activity')).toBeGreaterThan(0);
    });

    it('finds the choices of a list and the sections and sub-pages', () => {
        expect(searchAdmin('follow the device', ADMIN_SECTIONS).map(r => r.setting)).toContain('appearance.theme');
        expect(searchAdmin('potplayer', ADMIN_SECTIONS).map(r => r.setting)).toContain('player.externalPreset');
        const [moved] = searchAdmin('moved files', ADMIN_SECTIONS);
        expect(moved).toMatchObject({ title: 'Moved files', where: 'Duplicates & moves' });
        expect(moved.setting).toBeUndefined();
        expect(searchAdmin('my account', ADMIN_SECTIONS)[0]).toMatchObject({ title: 'My account', where: 'Account' });
    });

    it('only looks in the sections given', () => {
        const account = ADMIN_SECTIONS.filter(section => section.id === 'account');
        expect(searchAdmin('resolution', account)).toEqual([]);
    });
});

describe('markWords', () => {
    it('marks each match of the words, accents and case aside', () => {
        expect(markWords('Thème du thème', ['theme'])).toEqual([
            { text: 'Thème', match: true }, { text: ' du ', match: false }, { text: 'thème', match: true },
        ]);
        expect(markWords('Largest page', [])).toEqual([{ text: 'Largest page', match: false }]);
    });
});
