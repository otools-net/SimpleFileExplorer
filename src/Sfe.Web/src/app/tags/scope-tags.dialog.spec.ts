import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FolderDefaults, FolderDefaultTagsResult, ScopeTagChangeResult, ScopeTags, TagScope, TagSummary } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ApiMock, buttonNamed, limitedConfig, loadConfig, mockApi, settle, summary, tag, testProviders } from '../../testing/fakes';
import { ScopeTagsDialog } from './scope-tags.dialog';

describe('ScopeTagsDialog', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<ScopeTagsDialog>;
    let element: HTMLElement;
    let toasts: ToastService;
    let store: ListingStore;

    const beach = summary(tag('Beach'));
    const sun = summary(tag('sun'));
    const folder: TagScope = { path: 'Media', recursive: false, query: null };

    const section = (heading: string) => [...element.querySelectorAll('.tag-group')].find(g => g.querySelector('h3')!.textContent === heading);
    const chips = (heading: string) => [...section(heading)?.querySelectorAll('.tag-name') ?? []].map(chip => chip.textContent!.trim());
    const picker = () => element.querySelector<HTMLInputElement>('sfe-tag-picker:not(.default-picker) input');
    const defaultPicker = () => element.querySelector<HTMLInputElement>('sfe-tag-picker.default-picker input');
    const defaultSection = () => element.querySelector<HTMLElement>('.default-tags');
    const defaultConfirm = () => element.querySelector<HTMLElement>('.default-confirm');
    const defaultChips = () => [...defaultSection()!.querySelectorAll('.tag-name')].map(chip => chip.textContent!.trim());
    const noDefaults = (path = 'Media'): FolderDefaults => ({ path, own: [], inherited: [] });
    const options = () => [...element.querySelectorAll<HTMLLIElement>('.tag-options li')];
    const confirmBox = () => element.querySelector<HTMLElement>('.scope-confirm');
    const recursive = () => element.querySelector<HTMLInputElement>('.scope-recursive input');

    function tags(scope: TagScope, files: number, ...usages: [TagSummary, number][]): ScopeTags {
        return { scope, files, tags: usages.map(([tag, fileCount]) => ({ tag, fileCount })) };
    }

    function changed(scope: TagScope, files: number, added: number, removed: number): ScopeTagChangeResult {
        return { scope, files, added, removed };
    }

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        toasts = TestBed.inject(ToastService);
        store = TestBed.inject(ListingStore);
        api.tags.mockResolvedValue({ matches: [], descendants: [] });
        api.folderDefaultTags.mockResolvedValue(noDefaults());
        fixture = TestBed.createComponent(ScopeTagsDialog);
        element = fixture.nativeElement;
        await fixture.whenStable();
    });

    afterEach(() => vi.restoreAllMocks());

    async function open(scope: TagScope, answer: ScopeTags): Promise<void> {
        api.scopeTags.mockResolvedValue(answer);
        fixture.componentInstance.open(scope);
        await settle();
    }

    /** Picks `picked` in the tag picker. */
    async function pick(picked: TagSummary): Promise<void> {
        api.tags.mockResolvedValue({ matches: [tag(picked.name, { id: picked.id })], descendants: [] });
        picker()!.focus();
        await settle();
        options()[0].click();
        await settle();
    }

    it('registers itself so any part of the page can open it', () => {
        expect(TestBed.inject(Dialogs).scopeTags()).toBe(fixture.componentInstance);
    });

    it('counts the indexed files of a folder and the tags on all or some of them', async () => {
        await open(folder, tags(folder, 3, [beach, 3], [sun, 1]));

        expect(api.scopeTags).toHaveBeenCalledWith(folder, expect.any(AbortSignal));
        expect(element.querySelector('.sheet-subtitle')!.textContent).toBe('Files in “Media”');
        expect(element.querySelector('.scope-count')!.textContent).toContain('3 indexed files.');
        expect(chips('On all these files')).toEqual(['Beach']);
        expect(chips('On some of these files')).toEqual(['sun · 1']);
    });

    it('counts again with the sub-folders when asked', async () => {
        await open(folder, tags(folder, 3));
        const tree = { ...folder, recursive: true };
        api.scopeTags.mockResolvedValue(tags(tree, 10));

        recursive()!.click();
        await settle();

        expect(api.scopeTags).toHaveBeenLastCalledWith(tree, expect.any(AbortSignal));
        expect(element.querySelector('.sheet-subtitle')!.textContent).toBe('Files in “Media” and its sub-folders');
        expect(element.querySelector('.scope-count')!.textContent).toContain('10 indexed files.');
    });

    it('asks before adding a picked tag, and changes nothing when cancelled', async () => {
        await open(folder, tags(folder, 3, [sun, 1]));

        await pick(sun);

        expect(confirmBox()!.textContent).toContain('Add “sun” to every file in “Media”?');
        expect(confirmBox()!.textContent).toContain('1 file already has it.');
        expect(picker()).toBeNull();
        buttonNamed(confirmBox()!, 'Cancel')!.click();
        await settle();

        expect(confirmBox()).toBeNull();
        expect(picker()).not.toBeNull();
        expect(api.changeScopeTags).not.toHaveBeenCalled();
    });

    it('adds the confirmed tag to every file of the folder tree, then counts again', async () => {
        const tree = { ...folder, recursive: true };
        await open(tree, tags(tree, 4));
        const applied = vi.spyOn(store, 'applyScopeTags').mockImplementation(() => { });
        const show = vi.spyOn(toasts, 'show');
        api.changeScopeTags.mockResolvedValue(changed(tree, 4, 4, 0));

        await pick(beach);
        expect(confirmBox()!.textContent).toContain('Add “Beach” to every file in “Media” and its sub-folders?');
        api.scopeTags.mockResolvedValue(tags(tree, 4, [beach, 4]));
        buttonNamed(confirmBox()!, 'Add tag')!.click();
        await settle();

        expect(api.changeScopeTags).toHaveBeenCalledWith({ ...tree, add: [beach.id] });
        expect(show).toHaveBeenCalledWith('“Beach” added to 4 files.');
        expect(applied).toHaveBeenCalledWith(tree, { add: expect.objectContaining({ id: beach.id }) });
        expect(confirmBox()).toBeNull();
        expect(chips('On all these files')).toEqual(['Beach']);
    });

    it('asks before removing a tag from the files having it', async () => {
        await open(folder, tags(folder, 3, [sun, 2]));
        const applied = vi.spyOn(store, 'applyScopeTags').mockImplementation(() => { });
        api.changeScopeTags.mockResolvedValue(changed(folder, 3, 0, 2));

        section('On some of these files')!.querySelector<HTMLButtonElement>('.tag-remove')!.click();
        await settle();

        expect(confirmBox()!.textContent).toContain('Remove “sun” from the 2 files in “Media” that have it?');
        expect(api.changeScopeTags).not.toHaveBeenCalled();
        buttonNamed(confirmBox()!, 'Remove tag')!.click();
        await settle();

        expect(api.changeScopeTags).toHaveBeenCalledWith({ ...folder, remove: [sun.id] });
        expect(applied).toHaveBeenCalledWith(folder, { remove: sun.id });
    });

    it('tags the results of a search, with no sub-folder choice', async () => {
        const search: TagScope = { path: 'Media', recursive: false, query: 'cat' };
        await open({ ...search, query: ' cat ', recursive: true }, tags(search, 2, [sun, 1]));

        expect(api.scopeTags).toHaveBeenCalledWith(search, expect.any(AbortSignal));
        expect(recursive()).toBeNull();
        expect(element.querySelector('.sheet-subtitle')!.textContent).toBe('Search “cat” in “Media”');
        section('On some of these files')!.querySelector<HTMLButtonElement>('button.tag-name')!.click();
        await settle();

        expect(confirmBox()!.textContent).toContain('Add “sun” to the 2 files found by “cat” in “Media”?');
    });

    it('creates a new tag only once the change is confirmed', async () => {
        await open(folder, tags(folder, 1));
        const created = tag('Holiday');
        api.createTag.mockResolvedValue(created);
        api.changeScopeTags.mockResolvedValue(changed(folder, 1, 1, 0));
        vi.spyOn(store, 'applyScopeTags').mockImplementation(() => { });

        picker()!.focus();
        picker()!.value = 'Holiday';
        picker()!.dispatchEvent(new Event('input'));
        await settle(200);
        options().at(-1)!.click();
        await settle();

        expect(confirmBox()!.textContent).toContain('The tag is created first.');
        expect(api.createTag).not.toHaveBeenCalled();
        buttonNamed(confirmBox()!, 'Add tag')!.click();
        await settle();

        expect(api.createTag).toHaveBeenCalledWith({ name: 'Holiday' });
        expect(api.changeScopeTags).toHaveBeenCalledWith({ ...folder, add: [created.id] });
    });

    it('keeps the change waiting when a scan is running', async () => {
        await open(folder, tags(folder, 1));
        const error = vi.spyOn(toasts, 'error');
        api.changeScopeTags.mockResolvedValue({ detail: 'A scan is running: try again when it has finished.' });

        await pick(sun);
        buttonNamed(confirmBox()!, 'Add tag')!.click();
        await settle();

        expect(error).toHaveBeenCalledWith('A scan is running: try again when it has finished.');
        expect(confirmBox()).not.toBeNull();
    });

    describe('default tags', () => {
        /** Picks `picked` in the default tags' picker. */
        async function pickDefault(picked: TagSummary): Promise<void> {
            api.tags.mockResolvedValue({ matches: [tag(picked.name, { id: picked.id })], descendants: [] });
            defaultPicker()!.focus();
            await settle();
            options()[0].click();
            await settle();
        }

        function saved(defaults: FolderDefaults, files: ScopeTagChangeResult | null = null): FolderDefaultTagsResult {
            return { defaults, files };
        }

        it('lists the own default tags of the folder and those of its parents', async () => {
            api.folderDefaultTags.mockResolvedValue({ path: 'Media/Holidays', own: [beach], inherited: [{ path: 'Media', tag: sun }] });
            await open({ ...folder, path: 'Media/Holidays' }, tags(folder, 0));

            expect(api.folderDefaultTags).toHaveBeenCalledWith('Media/Holidays', expect.any(AbortSignal));
            expect(defaultChips()).toEqual(['Beach', 'sun · Media']);
            expect(defaultSection()!.querySelectorAll('.tag-remove')).toHaveLength(1);
        });

        it('adds a default tag for the new files only, or for the files already there too', async () => {
            await open(folder, tags(folder, 3));
            const refresh = vi.spyOn(store, 'refresh').mockResolvedValue();
            const applied = vi.spyOn(store, 'applyScopeTags').mockImplementation(() => { });
            const show = vi.spyOn(toasts, 'show');
            api.changeFolderDefaultTags.mockResolvedValue(saved({ ...noDefaults(), own: [beach] }));

            await pickDefault(beach);
            expect(defaultConfirm()!.textContent).toContain('Give “Beach” to every file put into “Media” or its sub-folders from now on?');
            expect(api.changeFolderDefaultTags).not.toHaveBeenCalled();
            buttonNamed(defaultConfirm()!, 'New files only')!.click();
            await settle();

            expect(api.changeFolderDefaultTags).toHaveBeenCalledWith({ path: 'Media', add: [beach.id], addToFiles: false });
            expect(show).toHaveBeenCalledWith('“Beach” is a default tag of “Media”: new files there get it.');
            expect(applied).not.toHaveBeenCalled();
            expect(refresh).toHaveBeenCalled();
            expect(defaultConfirm()).toBeNull();
            expect(defaultChips()).toEqual(['Beach']);

            api.changeFolderDefaultTags.mockResolvedValue(saved({ ...noDefaults(), own: [beach, sun] }, changed({ ...folder, recursive: true }, 3, 3, 0)));
            await pickDefault(sun);
            buttonNamed(defaultConfirm()!, 'Existing files too')!.click();
            await settle();

            expect(api.changeFolderDefaultTags).toHaveBeenLastCalledWith({ path: 'Media', add: [sun.id], addToFiles: true });
            expect(show).toHaveBeenLastCalledWith('“sun” is a default tag of “Media”: added to 3 files.');
            expect(applied).toHaveBeenCalledWith({ path: 'Media', recursive: true, query: null }, { add: expect.objectContaining({ id: sun.id }) });
        });

        it('asks whether the files already there keep a default tag taken off the folder', async () => {
            api.folderDefaultTags.mockResolvedValue({ ...noDefaults(), own: [beach] });
            await open(folder, tags(folder, 3, [beach, 3]));
            vi.spyOn(store, 'refresh').mockResolvedValue();
            const applied = vi.spyOn(store, 'applyScopeTags').mockImplementation(() => { });
            const show = vi.spyOn(toasts, 'show');
            api.changeFolderDefaultTags.mockResolvedValue(saved(noDefaults(), changed({ ...folder, recursive: true }, 3, 0, 3)));

            defaultSection()!.querySelector<HTMLButtonElement>('.tag-remove')!.click();
            await settle();

            expect(defaultConfirm()!.textContent).toContain('Stop giving “Beach” to the files put into “Media”?');
            expect(buttonNamed(defaultConfirm()!, 'Keep it on the files')).toBeDefined();
            buttonNamed(defaultConfirm()!, 'Remove it from the files too')!.click();
            await settle();

            expect(api.changeFolderDefaultTags).toHaveBeenCalledWith({ path: 'Media', remove: [beach.id], removeFromFiles: true });
            expect(show).toHaveBeenCalledWith('“Beach” is no longer a default tag of “Media”: removed from 3 files.');
            expect(applied).toHaveBeenCalledWith({ path: 'Media', recursive: true, query: null }, { remove: beach.id });
            expect(defaultSection()!.textContent).toContain('This folder has no default tags.');
        });

        it('keeps the change waiting when a scan is running', async () => {
            await open(folder, tags(folder, 1));
            const error = vi.spyOn(toasts, 'error');
            api.changeFolderDefaultTags.mockResolvedValue({ detail: 'A scan is running: try again when it has finished.' });

            await pickDefault(sun);
            buttonNamed(defaultConfirm()!, 'Existing files too')!.click();
            await settle();

            expect(error).toHaveBeenCalledWith('A scan is running: try again when it has finished.');
            expect(defaultConfirm()).not.toBeNull();
        });

        it('is not offered for a search', async () => {
            await open({ ...folder, query: 'cat' }, tags({ ...folder, query: 'cat' }, 2));

            expect(defaultSection()).toBeNull();
            expect(api.folderDefaultTags).not.toHaveBeenCalled();
        });

        it('shows the default tags without changing them, or nothing, without the right', async () => {
            await loadConfig(api, limitedConfig([Rights.viewTags, Rights.assignTags]));
            await open(folder, tags(folder, 1));
            expect(defaultSection()).toBeNull();

            api.folderDefaultTags.mockResolvedValue({ ...noDefaults(), own: [beach] });
            await open(folder, tags(folder, 1));
            expect(defaultChips()).toEqual(['Beach']);
            expect(defaultSection()!.querySelector('.tag-remove')).toBeNull();
            expect(defaultPicker()).toBeNull();
        });

        it('offers only to change the new files without the right to tag files', async () => {
            await loadConfig(api, limitedConfig([Rights.viewTags, Rights.folderDefaultTags]));
            await open(folder, tags(folder, 1));

            await pickDefault(sun);

            expect(buttonNamed(defaultConfirm()!, 'New files only')).toBeDefined();
            expect(buttonNamed(defaultConfirm()!, 'Existing files too')).toBeUndefined();
        });
    });
});
