import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { TagSearchResults } from '../core/models';
import { Dialogs } from '../library/dialogs.service';
import { ApiMock, deferred, loadConfig, mockApi, settle, tag, testProviders, type } from '../../testing/fakes';
import { TagEditorDialog, TagEditResult } from './tag-editor.dialog';
import { TagManagerDialog } from './tag-manager.dialog';

describe('TagManagerDialog', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<TagManagerDialog>;
    let element: HTMLElement;
    let editor: { open: ReturnType<typeof vi.fn<(id?: number | null, defaults?: { name?: string }) => Promise<TagEditResult>>> };

    const results = (matches: TagSearchResults['matches'], descendants: TagSearchResults['descendants'] = []): TagSearchResults => ({ matches, descendants });
    const search = () => element.querySelector<HTMLInputElement>('input[type=search]')!;
    const rows = () => [...element.querySelectorAll<HTMLButtonElement>('.tag-row')];
    const rowNames = () => rows().map(row => row.querySelector('.tag-name')!.textContent!.trim());
    const text = () => element.querySelector('.tag-table')!.textContent!.replace(/\s+/g, ' ').trim();

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        fixture = TestBed.createComponent(TagManagerDialog);
        element = fixture.nativeElement;
        editor = { open: vi.fn() };
        TestBed.inject(Dialogs).tagEditor.set(editor as unknown as TagEditorDialog);
        await fixture.whenStable();
    });

    async function open(answer: TagSearchResults = results([])): Promise<void> {
        api.tags.mockResolvedValue(answer);
        fixture.componentInstance.open();
        await settle();
    }

    it('registers itself so other parts of the page can open it', () => {
        expect(TestBed.inject(Dialogs).tagManager()).toBe(fixture.componentInstance);
    });

    it('unregisters when destroyed', () => {
        fixture.destroy();
        expect(TestBed.inject(Dialogs).tagManager()).toBeNull();
    });

    it('opens on every tag, with the search box focused', async () => {
        await open(results([tag('Animal'), tag('Cat')]));

        expect(element.querySelector('dialog')!.open).toBe(true);
        expect(document.activeElement).toBe(search());
        expect(api.tags).toHaveBeenCalledWith({ q: '', limit: 1000 });
        expect(rowNames()).toEqual(['Animal', 'Cat']);
    });

    it('describes each tag: shorthand, aliases, parents, properties and file count', async () => {
        const parent = tag('League of Legends', { shorthand: 'LoL' });
        await open(results([
            tag('Jinx Piltover', { shorthand: 'Jinx', aliases: ['Jinxy', 'Powder'], parents: [parent], fileCount: 1 }),
            tag('Character', { isCategory: true, isHidden: true, fileCount: 1234 }),
            tag('Bare'),
        ]));

        const details = rows().map(row => row.querySelector('.tag-row-detail')!.textContent!.trim());
        const counts = rows().map(row => row.querySelector('.tag-row-count')!.textContent!.trim());
        expect(details).toEqual([
            'Shorthand: Jinx · Aliases: Jinxy, Powder · Parents: League of Legends',
            'category, hidden',
            '',
        ]);
        expect(counts).toEqual(['1 file', `${(1234).toLocaleString()} files`, '0 files']);
        expect(rows()[0].title).toBe('Edit Jinx Piltover');
    });

    it('shows the disambiguated name of a tag', async () => {
        await open(results([tag('Freddy', { displayName: 'Freddy (FNAF)' })]));

        expect(rowNames()).toEqual(['Freddy (FNAF)']);
    });

    it('lists the tags inheriting from the matches apart', async () => {
        await open(results([tag('Animal')], [tag('Cat'), tag('Dog')]));

        expect(element.querySelector('.tag-table-heading')!.textContent).toBe('Inheriting from these');
        expect(rowNames()).toEqual(['Animal', 'Cat', 'Dog']);
    });

    it('lists only the descendants when nothing matches directly', async () => {
        await open(results([], [tag('Cat')]));

        expect(rowNames()).toEqual(['Cat']);
        expect(text()).not.toContain('No tag matches');
    });

    it('says when no tag matches', async () => {
        await open(results([]));

        expect(rows()).toHaveLength(0);
        expect(text()).toBe('No tag matches.');
        expect(element.querySelector('.tag-table-heading')).toBeNull();
    });

    it('shows names as text, never as markup', async () => {
        await open(results([tag('<img src=x onerror=alert(1)>')]));

        expect(element.querySelector('.tag-table img')).toBeNull();
        expect(rowNames()).toEqual(['<img src=x onerror=alert(1)>']);
    });

    it('searches as the user types, once the typing pauses, with the text trimmed', async () => {
        await open();
        api.tags.mockClear();
        api.tags.mockResolvedValue(results([tag('Shrek')]));

        type(search(), 'sh');
        type(search(), 'shr');
        type(search(), '  shrek  ');
        await settle(50);
        expect(api.tags).not.toHaveBeenCalled();
        await settle(200);

        expect(api.tags).toHaveBeenCalledTimes(1);
        expect(api.tags).toHaveBeenCalledWith({ q: 'shrek', limit: 1000 });
        expect(rowNames()).toEqual(['Shrek']);
    });

    it('lists every tag again when the search is emptied', async () => {
        await open();
        api.tags.mockResolvedValue(results([tag('A')]));
        type(search(), '   ');
        await settle(200);

        expect(api.tags).toHaveBeenLastCalledWith({ q: '', limit: 1000 });
    });

    it('keeps the results of the last search when an earlier one answers late', async () => {
        await open();
        const slow = deferred<TagSearchResults>();
        const fast = deferred<TagSearchResults>();
        api.tags.mockReturnValueOnce(slow.promise).mockReturnValueOnce(fast.promise);

        type(search(), 'c');
        await settle(200);
        type(search(), 'cat');
        await settle(200);
        fast.resolve(results([tag('Cat')]));
        await settle();
        slow.resolve(results([tag('Cactus'), tag('Cat')]));
        await settle();

        expect(rowNames()).toEqual(['Cat']);
    });

    it('ignores the failure of a search that was replaced', async () => {
        await open();
        const slow = deferred<TagSearchResults>();
        api.tags.mockReturnValueOnce(slow.promise).mockResolvedValueOnce(results([tag('Cat')]));

        type(search(), 'c');
        await settle(200);
        type(search(), 'cat');
        await settle(200);
        slow.reject(new ApiError(500, 'Boom'));
        await settle();

        expect(text()).not.toContain('Could not load tags');
        expect(rowNames()).toEqual(['Cat']);
    });

    it('reports a failed load, and recovers on the next search', async () => {
        api.tags.mockRejectedValue(new ApiError(0, 'The server is not reachable'));
        fixture.componentInstance.open();
        await settle();

        expect(text()).toBe('Could not load tags: The server is not reachable');
        expect(rows()).toHaveLength(0);

        api.tags.mockResolvedValue(results([tag('Back')]));
        type(search(), 'b');
        await settle(200);

        expect(text()).not.toContain('Could not load');
        expect(rowNames()).toEqual(['Back']);
    });

    it('edits a tag on click, and reloads after it was saved', async () => {
        const cat = tag('Cat');
        await open(results([cat]));
        editor.open.mockResolvedValue({ ...cat, name: 'Kitty', displayName: 'Kitty' });
        api.tags.mockResolvedValue(results([{ ...cat, name: 'Kitty', displayName: 'Kitty' }]));

        rows()[0].click();
        await settle();

        expect(editor.open).toHaveBeenCalledWith(cat.id);
        expect(api.tags).toHaveBeenCalledTimes(2);
        expect(rowNames()).toEqual(['Kitty']);
    });

    it('reloads after a tag was deleted from the editor', async () => {
        const cat = tag('Cat');
        await open(results([cat]));
        editor.open.mockResolvedValue({ deleted: cat.id });
        api.tags.mockResolvedValue(results([]));

        rows()[0].click();
        await settle();

        expect(api.tags).toHaveBeenCalledTimes(2);
        expect(text()).toBe('No tag matches.');
    });

    it('does not reload when the editor was cancelled', async () => {
        await open(results([tag('Cat')]));
        editor.open.mockResolvedValue(null);

        rows()[0].click();
        await settle();

        expect(api.tags).toHaveBeenCalledTimes(1);
    });

    it('creates a tag named after the search', async () => {
        await open();
        editor.open.mockResolvedValue(tag('Brand new'));
        type(search(), '  Brand new ');
        await settle(200);
        api.tags.mockClear();

        element.querySelector<HTMLButtonElement>('button.primary')!.click();
        await settle();

        expect(editor.open).toHaveBeenCalledWith(null, { name: 'Brand new' });
        expect(api.tags).toHaveBeenCalledWith({ q: 'Brand new', limit: 1000 });
    });

    it('creates an unnamed tag when nothing is searched', async () => {
        await open();
        editor.open.mockResolvedValue(null);

        element.querySelector<HTMLButtonElement>('button.primary')!.click();
        await settle();

        expect(editor.open).toHaveBeenCalledWith(null, { name: '' });
        expect(api.tags).toHaveBeenCalledTimes(1);
    });

    it('does nothing on a click while the editor is not on the page', async () => {
        TestBed.inject(Dialogs).tagEditor.set(null);
        await open(results([tag('Cat')]));

        rows()[0].click();
        element.querySelector<HTMLButtonElement>('button.primary')!.click();
        await settle();

        expect(api.tags).toHaveBeenCalledTimes(1);
    });

    it('closes from its Close button, and loads fresh tags when opened again', async () => {
        await open(results([tag('Old')]));
        const close = [...element.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent!.trim() === 'Close')!;

        close.click();
        expect(element.querySelector('dialog')!.open).toBe(false);

        await open(results([tag('Old'), tag('New')]));
        expect(element.querySelector('dialog')!.open).toBe(true);
        expect(rowNames()).toEqual(['Old', 'New']);
    });

    it('keeps the search text when opened again', async () => {
        await open();
        type(search(), 'cat');
        await settle(200);
        element.querySelector<HTMLButtonElement>('.sheet-head button')!.click();

        await open();

        expect(api.tags).toHaveBeenLastCalledWith({ q: 'cat', limit: 1000 });
    });
});
