import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScopeTagChangeResult, ScopeTags, TagScope, TagSummary } from '../core/models';
import { ToastService } from '../core/toast.service';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ApiMock, buttonNamed, loadConfig, mockApi, settle, summary, tag, testProviders } from '../../testing/fakes';
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
    const picker = () => element.querySelector<HTMLInputElement>('sfe-tag-picker input');
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
});
