import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { FileTags, TagSummary } from '../core/models';
import { ToastService } from '../core/toast.service';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ApiMock, applyChange, buttonNamed, deferred, fileTags, item, limitedConfig, loadConfig, mockApi, settle, summary, tag, testProviders } from '../../testing/fakes';
import { Rights } from '../core/rights';
import { FileTagsDialog } from './file-tags.dialog';
import { TagEditorDialog } from './tag-editor.dialog';
import { TagManagerDialog } from './tag-manager.dialog';

describe('FileTagsDialog', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<FileTagsDialog>;
    let element: HTMLElement;
    let toasts: ToastService;
    let store: ListingStore;

    const photo = item('Media/Ünïcødé #1 & 100%.jpg');
    const character = summary(tag('Character', { isCategory: true }));
    const shrek = summary(tag('Shrek'));
    const swamp = summary(tag('Swamp'));
    const known: TagSummary[] = [character, shrek, swamp];
    let current: Map<string, TagSummary[]>;

    const groups = () => [...element.querySelectorAll('.tag-group')].map(group => ({
        heading: group.querySelector('h3')!.textContent,
        tags: [...group.querySelectorAll('.tag-name')].map(chip => chip.textContent!.trim()),
    }));
    const picker = () => element.querySelector<HTMLInputElement>('sfe-tag-picker input')!;
    const options = () => [...element.querySelectorAll<HTMLLIElement>('.tag-options li')];
    const muted = () => element.querySelector('.tag-groups .muted')?.textContent ?? null;

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        toasts = TestBed.inject(ToastService);
        store = TestBed.inject(ListingStore);
        current = new Map();
        api.changeFileTags.mockImplementation(async change => applyChange(current, known, change));
        api.tags.mockResolvedValue({ matches: [], descendants: [] });
        fixture = TestBed.createComponent(FileTagsDialog);
        element = fixture.nativeElement;
        await fixture.whenStable();
    });

    afterEach(() => vi.restoreAllMocks());

    async function open(tags: FileTags = fileTags(photo.path)): Promise<void> {
        current.set(photo.path, tags.tags);
        api.fileTags.mockResolvedValue(tags);
        await fixture.componentInstance.open(photo);
        await settle();
    }

    it('shows a spinner while the tags load, named after the file', async () => {
        api.fileTags.mockReturnValue(new Promise(() => { }));
        void fixture.componentInstance.open(photo);
        await settle();

        expect(element.querySelector('dialog')!.open).toBe(true);
        expect(element.querySelector('.sheet-subtitle')!.textContent).toBe('Ünïcødé #1 & 100%.jpg');
        expect(element.querySelector('.tag-groups .spinner')).not.toBeNull();
        expect(api.fileTags).toHaveBeenCalledWith('Media/Ünïcødé #1 & 100%.jpg');
        expect(document.activeElement).toBe(picker());
    });

    it('invites to add a tag to a file without any', async () => {
        await open();

        expect(muted()).toContain('No tags yet');
        expect(groups()).toEqual([]);
    });

    it('groups the tags by category, the uncategorized ones under "Tags"', async () => {
        await open({
            path: photo.path,
            tags: [character, shrek, swamp],
            groups: [{ category: character, tags: [character, shrek] }, { category: null, tags: [swamp] }],
        });

        expect(groups()).toEqual([
            { heading: 'Character', tags: ['Character', 'Shrek'] },
            { heading: 'Tags', tags: ['Swamp'] },
        ]);
    });

    it('reports tags that could not be loaded', async () => {
        const error = vi.spyOn(toasts, 'error');
        api.fileTags.mockRejectedValue(new ApiError(404, 'Not Found'));

        await fixture.componentInstance.open(photo);

        expect(error).toHaveBeenCalledWith('Could not load the tags: Not Found');
    });

    it('does not show the tags of the file opened before while loading', async () => {
        await open(fileTags(photo.path, [shrek]));
        api.fileTags.mockReturnValue(new Promise(() => { }));

        void fixture.componentInstance.open(item('other.jpg'));
        await settle();

        expect(groups()).toEqual([]);
        expect(element.querySelector('.tag-groups .spinner')).not.toBeNull();
    });

    it('adds a picked tag, and updates the listing', async () => {
        const applied = vi.spyOn(store, 'applyFileTags');
        await open();
        api.tags.mockResolvedValue({ matches: [tag('Shrek', { id: shrek.id })], descendants: [] });

        // Focused when opened: focus it again to load the suggestions.
        picker().blur();
        picker().focus();
        await settle();
        options()[0].click();
        await settle();

        expect(api.changeFileTags).toHaveBeenCalledWith({ paths: [photo.path], add: [shrek.id] });
        expect(groups()).toEqual([{ heading: 'Tags', tags: ['Shrek'] }]);
        expect(applied).toHaveBeenCalledWith([fileTags(photo.path, [shrek])]);
    });

    it('does not offer the tags the file already has', async () => {
        await open(fileTags(photo.path, [shrek]));
        api.tags.mockResolvedValue({ matches: [tag('Shrek', { id: shrek.id }), tag('Swamp', { id: swamp.id })], descendants: [] });

        // Focused when opened: focus it again to load the suggestions.
        picker().blur();
        picker().focus();
        await settle();

        expect(options().map(li => li.textContent!.trim())).toEqual(['Swamp']);
    });

    it('creates a tag typed by name, then adds it', async () => {
        const show = vi.spyOn(toasts, 'show');
        const created = tag('Donkey');
        known.push(summary(created));
        api.createTag.mockResolvedValue(created);
        await open();
        api.tags.mockResolvedValue({ matches: [], descendants: [] });

        picker().focus();
        picker().value = 'Donkey';
        picker().dispatchEvent(new Event('input'));
        await settle(200);
        options()[0].click();
        await settle();

        expect(api.createTag).toHaveBeenCalledWith({ name: 'Donkey' });
        expect(show).toHaveBeenCalledWith('Tag “Donkey” created.');
        expect(api.changeFileTags).toHaveBeenCalledWith({ paths: [photo.path], add: [created.id] });
        expect(groups()).toEqual([{ heading: 'Tags', tags: ['Donkey'] }]);
    });

    it('tags nothing when creating the tag fails', async () => {
        const error = vi.spyOn(toasts, 'error');
        api.createTag.mockRejectedValue(new ApiError(400, 'A tag needs a name.'));
        await open();

        picker().focus();
        picker().value = 'x';
        picker().dispatchEvent(new Event('input'));
        await settle(200);
        options()[0].click();
        await settle();

        expect(error).toHaveBeenCalledWith('A tag needs a name.');
        expect(api.changeFileTags).not.toHaveBeenCalled();
    });

    it('removes a tag from its × button', async () => {
        await open(fileTags(photo.path, [shrek, swamp]));

        element.querySelector<HTMLButtonElement>('[aria-label="Remove Shrek"]')!.click();
        await settle();

        expect(api.changeFileTags).toHaveBeenCalledWith({ paths: [photo.path], remove: [shrek.id] });
        expect(groups()).toEqual([{ heading: 'Tags', tags: ['Swamp'] }]);
    });

    it('reports a removal the server refused, keeping the tag shown', async () => {
        const error = vi.spyOn(toasts, 'error');
        api.changeFileTags.mockRejectedValue(new ApiError(400, '“x” is not a file of the library.'));
        await open(fileTags(photo.path, [shrek]));

        element.querySelector<HTMLButtonElement>('[aria-label="Remove Shrek"]')!.click();
        await settle();

        expect(error).toHaveBeenCalledWith('“x” is not a file of the library.');
        expect(groups()).toEqual([{ heading: 'Tags', tags: ['Shrek'] }]);
    });

    it('edits a tag from its chip, then shows the file\'s tags again', async () => {
        const editor = { open: vi.fn(() => Promise.resolve(null)) };
        TestBed.inject(Dialogs).tagEditor.set(editor as unknown as TagEditorDialog);
        await open(fileTags(photo.path, [shrek]));
        api.fileTags.mockResolvedValue(fileTags(photo.path, [{ ...shrek, name: 'Shrek!', displayName: 'Shrek!' }]));

        element.querySelector<HTMLButtonElement>('button.tag-name')!.click();
        await settle();

        expect(editor.open).toHaveBeenCalledWith(shrek.id);
        expect(groups()).toEqual([{ heading: 'Tags', tags: ['Shrek!'] }]);
    });

    it('does not reload after editing when it was closed meanwhile', async () => {
        const editing = deferred<null>();
        const editor = { open: vi.fn(() => editing.promise) };
        TestBed.inject(Dialogs).tagEditor.set(editor as unknown as TagEditorDialog);
        await open(fileTags(photo.path, [shrek]));

        element.querySelector<HTMLButtonElement>('button.tag-name')!.click();
        element.querySelector('dialog')!.close();
        editing.resolve(null);
        await settle();

        expect(api.fileTags).toHaveBeenCalledTimes(1);
    });

    it('opens the tag manager', async () => {
        const manager = { open: vi.fn() };
        TestBed.inject(Dialogs).tagManager.set(manager as unknown as TagManagerDialog);
        await open();

        [...element.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent!.trim() === 'Manage tags')!.click();

        expect(manager.open).toHaveBeenCalled();
    });

    it('only shows the tags without the right to change them', async () => {
        await loadConfig(api, limitedConfig([Rights.browse, Rights.viewTags]));
        await open(fileTags(photo.path, [shrek]));

        expect(groups()).toEqual([{ heading: 'Tags', tags: ['Shrek'] }]);
        expect(element.querySelector<HTMLElement>('sfe-tag-picker')!.hidden).toBe(true);
        expect(element.querySelector('.tag-remove')).toBeNull();
        expect(element.querySelector('button.tag-name')).toBeNull();
        expect(buttonNamed(element, 'Manage tags')).toBeUndefined();
    });

    it('adds existing tags without the right to create them', async () => {
        await loadConfig(api, limitedConfig([Rights.browse, Rights.viewTags, Rights.assignTags]));
        await open();

        expect(element.querySelector<HTMLElement>('sfe-tag-picker')!.hidden).toBe(false);
        expect(buttonNamed(element, 'Manage tags')).toBeUndefined();
    });
});
