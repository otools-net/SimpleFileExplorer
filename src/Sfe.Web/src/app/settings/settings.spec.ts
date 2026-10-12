import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ApiError } from '../core/api.service';
import { SettingsValues, SettingsView } from '../core/models';
import { ToastService } from '../core/toast.service';
import { SearchHistory } from '../library/search-history.service';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { ADMIN_SECTIONS, locateSettings } from '../admin/admin-sections';
import { ApiMock, buttonNamed, adminPageProviders, deferred, loadConfig, mockApi, settle, type } from '../../testing/fakes';
import { EXTERNAL_PLAYERS, getValue, SettingField, setValue, SIMPLE_SETTINGS, TABS } from './settings-schema';

const FIELDS: SettingField[] = TABS.flatMap(tab => tab.groups.flatMap(group => group.fields));

/** Settings as the API sends them: every field of the schema, with a value of its type. */
function values(overrides: Record<string, unknown> = {}): SettingsValues {
    const result: Record<string, unknown> = {};
    for (const field of FIELDS) {
        if (field.type === 'preset')
            continue;
        const value = field.type === 'number' ? field.min
            : field.type === 'checkbox' ? false
                : field.type === 'list' || field.type === 'extensions' ? []
                    : field.type === 'select' ? field.options[0][0]
                        : null;
        const parts = field.key.split('.');
        let target = result;
        for (const part of parts.slice(0, -1))
            target = (target[part] ??= {}) as Record<string, unknown>;
        target[parts.at(-1)!] = value;
    }
    for (const [key, value] of Object.entries(overrides))
        setValue(result, key, value);
    return result;
}

describe('settings schema', () => {
    it('names each setting and each tab once', () => {
        const keys = FIELDS.map(field => field.key);
        expect(new Set(keys).size).toBe(keys.length);
        expect(new Set(TABS.map(tab => tab.id)).size).toBe(TABS.length);
    });

    it('shows in the simple view only settings that exist', () => {
        const keys = new Set(FIELDS.map(field => field.key));
        expect([...SIMPLE_SETTINGS].filter(key => !keys.has(key))).toEqual([]);
    });

    it('has a usable range for every number and choices for every list', () => {
        for (const field of FIELDS) {
            if (field.type === 'number')
                expect(field.min, field.key).toBeLessThanOrEqual(field.max);
            if (field.type === 'select')
                expect(field.options.length, field.key).toBeGreaterThan(0);
        }
    });

    it('fills existing text fields from its presets', () => {
        const keys = new Map(FIELDS.map(field => [field.key, field.type]));
        for (const field of FIELDS.filter(f => f.type === 'preset')) {
            if (field.type !== 'preset')
                continue;
            expect(keys.get(field.fills.url)).toBe('text');
            expect(keys.get(field.fills.name)).toBe('text');
        }
    });

    it('offers external players whose links have a placeholder and a safe scheme', () => {
        for (const [label, name, url] of EXTERNAL_PLAYERS) {
            expect(label && name, url).toBeTruthy();
            expect(url).toMatch(/\{(url|encodedUrl)\}/);
            expect(url).not.toMatch(/^(javascript|data|vbscript):/i);
        }
        expect(new Set(EXTERNAL_PLAYERS.map(p => p[2])).size).toBe(EXTERNAL_PLAYERS.length);
    });

    it('reads and writes nested values by key', () => {
        const target = { a: { b: { c: 1 } }, d: 2 };

        expect(getValue(target, 'a.b.c')).toBe(1);
        expect(getValue(target, 'd')).toBe(2);
        expect(getValue(target, 'a.x.c')).toBeUndefined();
        setValue(target, 'a.b.c', 5);
        setValue(target, 'd', null);
        expect(target).toEqual({ a: { b: { c: 5 } }, d: null });
    });
});

describe('settings on the administration page', () => {
    let api: ApiMock;
    let harness: RouterTestingHarness;
    let element: HTMLElement;
    let toasts: ToastService;

    const view = (overrides: Record<string, unknown> = {}, customized = false): SettingsView => ({ values: values(overrides), customized });
    const control = <T extends HTMLElement = HTMLInputElement>(key: string) => element.querySelector<T>(`#setting-${key.replaceAll('.', '-')}`)!;
    const navItem = (title: string) => [...element.querySelectorAll<HTMLAnchorElement>('.admin-nav > ul > li > a')].find(a => a.querySelector('span')!.textContent === title)!;
    const sectionShown = () => element.querySelector('.admin-head .admin-eyebrow')!.textContent;
    const currentSubItem = () => element.querySelector('.admin-subnav a[aria-current=page]')!;
    const subItem = (title: string) => [...element.querySelectorAll('.admin-subnav a')].find(a => a.querySelector('span')!.textContent === title)!;
    const fieldError = (key: string) => control(key).closest('.field, .check')!.querySelector('.field-error')?.textContent ?? null;
    const summary = () => element.querySelector('.settings-summary')?.textContent ?? null;
    const status = () => element.querySelector('.admin-savebar-status')!.textContent;
    const showAll = () => element.querySelector<HTMLInputElement>('.settings-show-all input')!;
    const bar = (label: string) => [...element.querySelectorAll<HTMLButtonElement>('.admin-savebar button')].find(b => b.textContent!.trim() === label);
    const save = async () => {
        bar('Save')!.click();
        await settle();
    };
    const select = async (key: string, value: string) => {
        const box = control<HTMLSelectElement>(key);
        box.value = value;
        box.dispatchEvent(new Event('change'));
        await settle();
    };
    /** The values of the last save. */
    const sent = () => api.saveSettings.mock.lastCall![0] as Record<string, unknown>;

    /** Shows the sub-page of the tab `settings` of settings-schema.ts. */
    async function go(settings: string): Promise<void> {
        const { section, subpage } = locateSettings(settings)!;
        await harness.navigateByUrl(`/admin?section=${section.id}&page=${subpage.id}`);
        await settle();
    }

    /** Opens the page (again, with a new form) on the tab `tab` of settings-schema.ts, the server sending `settings`. */
    async function open(settings: SettingsView = view(), tab = 'browsing'): Promise<void> {
        api.settings.mockResolvedValue(settings);
        await harness.navigateByUrl('/');
        await go(tab);
    }

    beforeEach(async () => {
        localStorage.clear();
        api = mockApi();
        TestBed.configureTestingModule({ providers: adminPageProviders(api) });
        await loadConfig(api);
        toasts = TestBed.inject(ToastService);
        api.trickplayStatus.mockResolvedValue({ state: 'disabled', checked: 0, generated: 0, failed: 0, queued: 0, pending: 0, discovering: false, current: [], lastPassFinished: null });
        harness = await RouterTestingHarness.create();
        element = harness.fixture.nativeElement;
    });

    afterEach(() => vi.restoreAllMocks());

    it('gives each tab of settings to one sub-page', () => {
        for (const tab of TABS)
            expect(ADMIN_SECTIONS.flatMap(s => s.subpages).filter(page => page.settings === tab.id), tab.id).toHaveLength(1);
    });

    it('fills each field from the settings, as its type shows it', async () => {
        await open(view({
            'browse.defaultPageSize': 250,
            'search.explain': true,
            'dataServer': null,
            'scan.ignoredDirectories': ['$RECYCLE.BIN', 'node_modules'],
            'fileTypes.images': ['.jpg', '.png'],
            'appearance.theme': 'light',
        }));
        showAll().click();
        await settle();

        expect(control('browse.defaultPageSize').value).toBe('250');
        await go('search');
        expect(control('search.explain').checked).toBe(true);
        await go('playback');
        expect(control('dataServer').value).toBe('');
        await go('appearance');
        expect(control<HTMLSelectElement>('appearance.theme').value).toBe('light');
        await go('skipped');
        expect(control<HTMLTextAreaElement>('scan.ignoredDirectories').value).toBe('$RECYCLE.BIN\nnode_modules');
        await go('fileTypes');
        expect(control('fileTypes.images').value).toBe('.jpg, .png');
    });

    it('says whether saved settings or the server\'s configuration are used', async () => {
        await open(view({}, true));
        expect(status()).toBe('Using saved settings');
        expect(bar('Reset to defaults')!.disabled).toBe(false);

        await open(view({}, false));
        expect(status()).toBe('Using the server\'s configuration');
        expect(bar('Reset to defaults')!.disabled).toBe(true);
    });

    it('reports settings that could not be loaded, and loads them again on asking', async () => {
        api.settings.mockRejectedValue(new ApiError(500, 'Boom'));
        await harness.navigateByUrl('/admin?section=interface&page=browsing');
        await settle();

        expect(element.querySelector('.admin-panel .form-error')!.textContent).toBe('Could not load the settings: Boom');

        api.settings.mockResolvedValue(view());
        buttonNamed(element, 'Try again')!.click();
        await settle();
        expect(control('browse.defaultPageSize')).not.toBeNull();
    });

    it('shows the most used settings only, until asked for all of them (remembered)', async () => {
        await open();
        const hiddenField = control('browse.maxPageSize').closest<HTMLElement>('.field')!;
        const simpleField = control('browse.defaultPageSize').closest<HTMLElement>('.field')!;

        expect(simpleField.hidden).toBe(false);
        expect(hiddenField.hidden).toBe(true);

        showAll().click();
        await settle();
        expect(hiddenField.hidden).toBe(false);
        expect(localStorage.getItem('sfe.settings.showAll')).toBe('1');
    });

    it('points to the full view in a sub-page whose settings it all hides', async () => {
        for (const tab of TABS) {
            await open(view(), tab.id);
            const simple = tab.groups.some(g => g.fields.some(f => SIMPLE_SETTINGS.has(f.key)));
            expect(element.querySelector('.settings-advanced-note') === null, tab.id).toBe(simple);
        }
    });

    it('keeps the changes made in several sections, and saves them all cleaned up, with the values it has no field for', async () => {
        api.saveSettings.mockImplementation(async settings => ({ values: settings, customized: true }));
        await open({ values: { ...values({ 'browse.defaultPageSize': 100 }), unknownFutureSetting: { kept: true } }, customized: false });
        showAll().click();
        await settle();
        expect(bar('Save')!.disabled).toBe(true);

        type(control('browse.defaultPageSize'), '300');
        await go('playback');
        type(control('dataServer'), '   ');
        type(control('player.externalName'), '  VLC  ');
        await go('search');
        control('search.explain').click();
        await go('skipped');
        type(control<HTMLTextAreaElement>('scan.ignoredFiles'), ' Thumbs.db \n\n desktop.ini \n');
        await go('fileTypes');
        type(control('fileTypes.videos'), '.mp4,  .mkv  .webm,,');
        await settle();
        expect(status()).toBe('Not saved yet: Interface › Browsing, Interface › Search, Playback, Scanning › What to scan, Scanning › File types');
        // Marked on the sections, and on the sub-pages of the open one.
        expect(navItem('Interface').querySelector('.admin-mark')).not.toBeNull();
        expect(navItem('Playback').querySelector('.admin-mark')).not.toBeNull();
        expect(navItem('Thumbnails').querySelector('.admin-mark')).toBeNull();
        expect(subItem('File types').querySelector('.admin-mark')).not.toBeNull();
        expect(subItem('Indexing').querySelector('.admin-mark')).toBeNull();
        api.config.mockClear();
        await save();

        expect(getValue(sent(), 'browse.defaultPageSize')).toBe(300);
        expect(getValue(sent(), 'dataServer')).toBeNull();
        expect(getValue(sent(), 'player.externalName')).toBe('VLC');
        expect(getValue(sent(), 'scan.ignoredFiles')).toEqual(['Thumbs.db', 'desktop.ini']);
        expect(getValue(sent(), 'fileTypes.videos')).toEqual(['.mp4', '.mkv', '.webm']);
        expect(getValue(sent(), 'search.explain')).toBe(true);
        expect(sent()['unknownFutureSetting']).toEqual({ kept: true });
        // What the client derives from the settings is read again.
        expect(api.config).toHaveBeenCalled();
        expect(status()).toBe('Using saved settings');
        expect(bar('Save')!.disabled).toBe(true);
    });

    it('saves on Enter in a field', async () => {
        api.saveSettings.mockImplementation(async settings => ({ values: settings, customized: true }));
        await open();
        type(control('browse.defaultPageSize'), '300');

        element.querySelector('form.settings-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        expect(getValue(sent(), 'browse.defaultPageSize')).toBe(300);
    });

    it('discards the changes not saved', async () => {
        await open(view({ 'browse.defaultPageSize': 100 }));
        type(control('browse.defaultPageSize'), '300');
        await settle();

        bar('Discard')!.click();
        await settle();

        expect(control('browse.defaultPageSize').value).toBe('100');
        expect(bar('Discard')).toBeUndefined();
        expect(bar('Save')!.disabled).toBe(true);
    });

    it('refuses numbers that are not whole before asking the server', async () => {
        await open();

        type(control('browse.defaultPageSize'), '');
        await go('playback');
        type(control('player.seekSeconds'), '2.5');
        await settle();
        await save();

        expect(api.saveSettings).not.toHaveBeenCalled();
        expect(fieldError('player.seekSeconds')).toBe('Enter a whole number.');
        expect(summary()).toBe('Some settings need fixing (see the marked sections).');
        expect(navItem('Interface').classList).toContain('has-error');
        await go('browsing');
        expect(fieldError('browse.defaultPageSize')).toBe('Enter a whole number.');
        expect(subItem('Browsing').classList).toContain('has-error');
        expect(subItem('Search').classList).not.toContain('has-error');
    });

    it('shows the server\'s errors by their fields, opening their sub-page, and the others in the summary', async () => {
        api.saveSettings.mockRejectedValue(new ApiError(400, 'x', {
            'trickplay.width': ['Between 64 and 1280.'],
            'somethingElse': ['The settings file is read-only.'],
        }));
        await open();
        type(control('browse.defaultPageSize'), '300');
        await settle();

        await save();

        expect(sectionShown()).toBe('Trickplay');
        expect(currentSubItem().textContent).toContain('Settings');
        expect(currentSubItem().classList).toContain('has-error');
        // A setting of the full view: shown so that it can be fixed.
        expect(showAll().checked).toBe(true);
        expect(fieldError('trickplay.width')).toBe('Between 64 and 1280.');
        expect(summary()).toBe('Some settings need fixing (see the marked sections). The settings file is read-only.');
        expect(navItem('Trickplay').classList).toContain('has-error');
        expect(navItem('Interface').classList).not.toContain('has-error');
        // Still not saved.
        expect(navItem('Interface').querySelector('.admin-mark')).not.toBeNull();
    });

    it('shows an error about a group of fields above the group', async () => {
        api.saveSettings.mockRejectedValue(new ApiError(400, 'x', { fileTypes: ['An extension is in two lists.'] }));
        await open(view(), 'fileTypes');
        type(control('fileTypes.videos'), '.mp4');
        await settle();

        await save();

        expect(element.querySelector('#settings-panel-fileTypes .settings-group .form-error')!.textContent).toBe('An extension is in two lists.');
    });

    it('stays on the current sub-page when it has errors too', async () => {
        api.saveSettings.mockRejectedValue(new ApiError(400, 'x', { 'tags.sidebarCount': ['Too many.'], 'trickplay.width': ['No.'] }));
        await open(view(), 'tags');
        type(control('tags.sidebarCount'), '19');
        await settle();

        await save();

        expect(sectionShown()).toBe('Tags');
        expect(currentSubItem().textContent).toContain('Settings');
    });

    it('reports a save that failed without validation errors', async () => {
        const error = vi.spyOn(toasts, 'error');
        api.saveSettings.mockRejectedValue(new ApiError(0, 'The server is not reachable'));
        await open();
        type(control('browse.defaultPageSize'), '300');
        await settle();

        await save();

        expect(error).toHaveBeenCalledWith('Could not save the settings: The server is not reachable');
        expect(bar('Save')!.disabled).toBe(false);
    });

    it('disables Save while saving', async () => {
        const pending = deferred<SettingsView>();
        api.saveSettings.mockReturnValue(pending.promise);
        await open();
        type(control('browse.defaultPageSize'), '300');
        await settle();

        void save();
        await settle();
        expect(bar('Save')!.disabled).toBe(true);

        pending.resolve(view());
        await settle();
        expect(status()).toBe('Using the server\'s configuration');
    });

    describe('external player presets', () => {
        it('fills the link and the name from a preset, and empties them for none', async () => {
            api.saveSettings.mockImplementation(async settings => ({ values: settings, customized: true }));
            await open(view(), 'playback');

            await select('player.externalPreset', '0');
            await save();
            expect(getValue(sent(), 'player.externalUrl')).toBe(EXTERNAL_PLAYERS[0][2]);
            expect(getValue(sent(), 'player.externalName')).toBe(EXTERNAL_PLAYERS[0][1]);

            await open(view({ 'player.externalUrl': 'vlc://{url}', 'player.externalName': 'VLC' }), 'playback');
            await select('player.externalPreset', '');
            await save();
            expect(getValue(sent(), 'player.externalUrl')).toBeNull();
            expect(getValue(sent(), 'player.externalName')).toBeNull();
        });

        it('shows the preset matching the link, "Custom…" for another link, none without one', async () => {
            const preset = () => control<HTMLSelectElement>('player.externalPreset').value;

            await open(view({ 'player.externalUrl': ` ${EXTERNAL_PLAYERS[4][2]} ` }), 'playback');
            expect(preset()).toBe('4');

            await open(view({ 'player.externalUrl': 'myplayer://{url}' }), 'playback');
            expect(preset()).toBe('custom');

            await open(view({ 'player.externalUrl': null }), 'playback');
            expect(preset()).toBe('');
        });

        it('disables the link and the name while the choice is none', async () => {
            const disabled = () => [control<HTMLInputElement>('player.externalUrl').disabled, control<HTMLInputElement>('player.externalName').disabled];
            await open(view({ 'player.externalUrl': null }), 'playback');
            expect(disabled()).toEqual([true, true]);

            await select('player.externalPreset', '0');
            expect(disabled()).toEqual([false, false]);

            await select('player.externalPreset', '');
            expect(disabled()).toEqual([true, true]);

            await open(view({ 'player.externalUrl': 'myplayer://{url}' }), 'playback');
            expect(disabled()).toEqual([false, false]);
        });

        it('keeps "Custom…" with an empty link, so that one can be written', async () => {
            const preset = () => control<HTMLSelectElement>('player.externalPreset').value;
            await open(view({ 'player.externalUrl': null }), 'playback');

            await select('player.externalPreset', 'custom');
            expect(preset()).toBe('custom');
            expect(control<HTMLInputElement>('player.externalUrl').disabled).toBe(false);

            type(control('player.externalUrl'), 'x://');
            type(control('player.externalUrl'), '');
            await settle();
            expect(preset()).toBe('custom');
            expect(control<HTMLInputElement>('player.externalUrl').disabled).toBe(false);
        });

        it('shows the link field to write a custom one', async () => {
            await open(view(), 'playback');
            expect(showAll().checked).toBe(false);

            await select('player.externalPreset', 'custom');

            expect(showAll().checked).toBe(true);
            expect(control('player.externalUrl').closest<HTMLElement>('.field')!.hidden).toBe(false);
            expect(document.activeElement).toBe(control('player.externalUrl'));
        });
    });

    it('resets to the server\'s configuration after asking', async () => {
        const ask = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
        api.resetSettings.mockResolvedValue(view({ 'browse.defaultPageSize': 100 }, false));
        await open(view({ 'browse.defaultPageSize': 999 }, true));
        api.config.mockClear();

        bar('Reset to defaults')!.click();
        await settle();
        expect(api.resetSettings).not.toHaveBeenCalled();

        bar('Reset to defaults')!.click();
        await settle();
        expect(ask).toHaveBeenCalledTimes(2);
        expect(control('browse.defaultPageSize').value).toBe('100');
        expect(bar('Reset to defaults')!.disabled).toBe(true);
        expect(api.config).toHaveBeenCalled();
    });

    it('asks before leaving with changes not saved', async () => {
        const ask = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
        await open();
        type(control('browse.defaultPageSize'), '300');
        await settle();

        await harness.navigateByUrl('/');
        expect(TestBed.inject(Router).url).toBe('/admin?section=interface&page=browsing');

        await harness.navigateByUrl('/');
        expect(ask).toHaveBeenCalledTimes(2);
        expect(TestBed.inject(Router).url).toBe('/');
    });

    it('shows the trickplay activity from its settings', async () => {
        api.trickplayActivity.mockResolvedValue({ status: { state: 'idle', checked: 0, generated: 0, failed: 0, queued: 0, pending: 0, discovering: false, current: [], lastPassFinished: null }, running: [], queued: [], pending: [], recent: [] });
        await open(view(), 'trickplay');

        buttonNamed(element, 'Show activity')!.click();
        await settle();

        expect(currentSubItem().textContent).toContain('Activity');
        expect(element.querySelector('sfe-trickplay-activity')).not.toBeNull();
    });

    it('clears the tag history after asking, then refreshes the sidebar', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const refresh = vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => { });
        const show = vi.spyOn(toasts, 'show');
        api.clearTagHistory.mockResolvedValue(undefined);
        await open(view(), 'tags');

        buttonNamed(element, 'Clear tag history')!.click();
        await settle();

        expect(api.clearTagHistory).toHaveBeenCalled();
        expect(refresh).toHaveBeenCalled();
        expect(show).toHaveBeenCalledWith('Tag history cleared.');
    });

    it('clears the search history of this browser after asking', async () => {
        const ask = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
        const history = TestBed.inject(SearchHistory);
        history.record('beach');
        await open(view(), 'search');

        buttonNamed(element, 'Clear search history')!.click();
        expect(history.entries()).toEqual(['beach']);
        buttonNamed(element, 'Clear search history')!.click();
        await settle();

        expect(ask).toHaveBeenCalledTimes(2);
        expect(history.entries()).toEqual([]);
        expect(localStorage.getItem('sfe.searchHistory')).toBe('[]');
    });

    it('reports a history that could not be cleared', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const error = vi.spyOn(toasts, 'error');
        api.clearTagHistory.mockRejectedValue(new ApiError(500, 'Boom'));
        await open(view(), 'tags');

        buttonNamed(element, 'Clear tag history')!.click();
        await settle();

        expect(error).toHaveBeenCalledWith('Could not clear the history: Boom');
    });
});
