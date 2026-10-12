import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { FileTagChangeResult, LibraryItem, TagSummary } from '../core/models';
import { ToastService } from '../core/toast.service';
import { Dialogs } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { ApiMock, applyChange, deferred, fakeConfig, item, loadConfig, mockApi, settle, summary, tag, testProviders } from '../../testing/fakes';
import { FilesTagsDialog } from './files-tags.dialog';
import { TagEditorDialog } from './tag-editor.dialog';

describe('FilesTagsDialog', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<FilesTagsDialog>;
    let element: HTMLElement;
    let toasts: ToastService;
    let store: ListingStore;
    let current: Map<string, TagSummary[]>;

    const beach = summary(tag('Beach'));
    const sun = summary(tag('sun'));
    const sea = summary(tag('Sea'));
    const known = [beach, sun, sea];

    const section = (heading: string) => [...element.querySelectorAll('.tag-group')].find(g => g.querySelector('h3')!.textContent === heading);
    const chips = (heading: string) => [...section(heading)?.querySelectorAll('.tag-name') ?? []].map(chip => chip.textContent!.trim());
    const picker = () => element.querySelector<HTMLInputElement>('sfe-tag-picker input')!;
    const options = () => [...element.querySelectorAll<HTMLLIElement>('.tag-options li')];

    async function setUp(maxFilesPerTagChange = 1000): Promise<void> {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, fakeConfig({ maxFilesPerTagChange }));
        toasts = TestBed.inject(ToastService);
        store = TestBed.inject(ListingStore);
        current = new Map();
        api.changeFileTags.mockImplementation(async change => applyChange(current, known, change));
        api.tags.mockResolvedValue({ matches: [], descendants: [] });
        fixture = TestBed.createComponent(FilesTagsDialog);
        element = fixture.nativeElement;
        await fixture.whenStable();
    }

    function files(...tagLists: (TagSummary[] | null)[]): LibraryItem[] {
        return tagLists.map((tags, index) => {
            const file = item(`Folder ${index % 2}/file ${index}.jpg`, { tags });
            current.set(file.path, tags ?? []);
            return file;
        });
    }

    async function open(selection: LibraryItem[]): Promise<void> {
        fixture.componentInstance.open(selection);
        await settle();
    }

    afterEach(() => vi.restoreAllMocks());

    describe('with the default part size', () => {
        beforeEach(() => setUp());

        it('counts the files in its subtitle', async () => {
            await open(files([], []));

            expect(element.querySelector('.sheet-subtitle')!.textContent).toBe('2 files');
            expect(document.activeElement).toBe(picker());
        });

        it('says when none of the files has tags (also when their tags are unknown)', async () => {
            await open(files([], null));

            expect(element.querySelector('.tag-groups .muted')!.textContent).toContain('None of these files has tags yet');
        });

        it('splits the tags on all the files from those on some, with how many have them', async () => {
            await open(files([beach, sun], [beach], [beach, sea, sun]));

            expect(chips('On all these files')).toEqual(['Beach']);
            // Most common first, then by name whatever the case.
            expect(chips('On some of these files')).toEqual(['sun · 2', 'Sea · 1']);
            const title = section('On some of these files')!.querySelector('.tag-name')!.getAttribute('title');
            expect(title).toBe('On 2 of 3 files. Click to add it to all of them.');
        });

        it('treats a single file\'s tags as on all of them', async () => {
            await open(files([sun]));

            expect(chips('On all these files')).toEqual(['sun']);
            expect(section('On some of these files')).toBeUndefined();
        });

        it('adds a picked tag to every file, across folders', async () => {
            const applied = vi.spyOn(store, 'applyFileTags');
            const selection = files([], [beach], []);
            await open(selection);
            api.tags.mockResolvedValue({ matches: [tag('Sea', { id: sea.id })], descendants: [] });

            // Focused when opened: focus it again to load the suggestions.
            picker().blur();
            picker().focus();
            await settle();
            options()[0].click();
            await settle();

            expect(api.changeFileTags).toHaveBeenCalledWith({ paths: selection.map(f => f.path), add: [sea.id] });
            expect(chips('On all these files')).toEqual(['Sea']);
            expect(chips('On some of these files')).toEqual(['Beach · 1']);
            expect(applied).toHaveBeenCalledTimes(1);
        });

        it('offers the tags on only some of the files, but not those on all', async () => {
            await open(files([beach, sun], [beach]));
            api.tags.mockResolvedValue({ matches: [tag('Beach', { id: beach.id }), tag('sun', { id: sun.id })], descendants: [] });

            // Focused when opened: focus it again to load the suggestions.
            picker().blur();
            picker().focus();
            await settle();

            expect(options().map(li => li.textContent!.trim())).toEqual(['sun']);
        });

        it('adds a tag on some files to all of them with a click', async () => {
            const selection = files([sun], []);
            await open(selection);

            section('On some of these files')!.querySelector<HTMLButtonElement>('button.tag-name')!.click();
            await settle();

            expect(api.changeFileTags).toHaveBeenCalledWith({ paths: selection.map(f => f.path), add: [sun.id] });
            expect(chips('On all these files')).toEqual(['sun']);
            expect(section('On some of these files')).toBeUndefined();
        });

        it('removes a tag from the files that have it only', async () => {
            const selection = files([sun, beach], [beach], [sun]);
            await open(selection);

            section('On some of these files')!.querySelector<HTMLButtonElement>('[aria-label="Remove sun · 2"]')!.click();
            await settle();

            expect(api.changeFileTags).toHaveBeenCalledWith({ paths: [selection[0].path, selection[2].path], remove: [sun.id] });
            expect(chips('On some of these files')).toEqual(['Beach · 2']);
        });

        it('creates a tag typed by name, then adds it to all the files', async () => {
            const created = tag('Holiday');
            known.push(summary(created));
            api.createTag.mockResolvedValue(created);
            const show = vi.spyOn(toasts, 'show');
            await open(files([], []));

            picker().focus();
            picker().value = 'Holiday';
            picker().dispatchEvent(new Event('input'));
            await settle(200);
            options().at(-1)!.click();
            await settle();

            expect(api.createTag).toHaveBeenCalledWith({ name: 'Holiday' });
            expect(show).toHaveBeenCalledWith('Tag “Holiday” created.');
            expect(chips('On all these files')).toEqual(['Holiday']);
        });

        it('reports a refused change and leaves the tags as they were', async () => {
            const error = vi.spyOn(toasts, 'error');
            api.changeFileTags.mockRejectedValue(new ApiError(400, 'Tag 9 does not exist.'));
            await open(files([sun], []));

            section('On some of these files')!.querySelector<HTMLButtonElement>('button.tag-name')!.click();
            await settle();

            expect(error).toHaveBeenCalledWith('Tag 9 does not exist.');
            expect(chips('On some of these files')).toEqual(['sun · 1']);
        });

        it('ignores a second change while one is being sent', async () => {
            const pending = deferred<FileTagChangeResult>();
            api.changeFileTags.mockReturnValueOnce(pending.promise);
            await open(files([sun, beach], []));

            const someChips = section('On some of these files')!.querySelectorAll<HTMLButtonElement>('button.tag-name');
            someChips[0].click();
            someChips[1].click();
            pending.resolve({ added: 0, removed: 0, files: [] });
            await settle();

            expect(api.changeFileTags).toHaveBeenCalledTimes(1);
        });

        it('shows the edited tag after the editor closes, as the listing has it', async () => {
            const selection = files([sun]);
            const editor = {
                open: vi.fn(async () => {
                    store.items.set([{ ...selection[0], tags: [{ ...sun, displayName: 'Sunny' }] }]);
                    return null;
                }),
            };
            TestBed.inject(Dialogs).tagEditor.set(editor as unknown as TagEditorDialog);
            await open(selection);

            section('On all these files')!.querySelector<HTMLButtonElement>('button.tag-name')!.click();
            await settle();

            expect(editor.open).toHaveBeenCalledWith(sun.id);
            expect(chips('On all these files')).toEqual(['Sunny']);
        });

        it('starts afresh with each selection', async () => {
            await open(files([sun], [sun]));
            await open(files([]));

            expect(element.querySelector('.sheet-subtitle')!.textContent).toBe('1 file');
            expect(section('On all these files')).toBeUndefined();
        });
    });

    describe('with a part size of 2', () => {
        beforeEach(() => setUp(2));

        it('sends a large selection in parts, in order', async () => {
            const selection = files([], [], [], [], []);
            await open(selection);

            await fixture.componentInstance['change']({ add: [sea.id] });
            await settle();

            expect(api.changeFileTags.mock.calls.map(([change]) => change.paths)).toEqual([
                [selection[0].path, selection[1].path],
                [selection[2].path, selection[3].path],
                [selection[4].path],
            ]);
            expect(chips('On all these files')).toEqual(['Sea']);
        });

        it('shows its progress while parts are being sent', async () => {
            const first = deferred<FileTagChangeResult>();
            api.changeFileTags.mockReturnValueOnce(first.promise);
            await open(files([], [], []));
            const spinner = element.querySelector<HTMLElement>('.tag-loading')!;
            expect(spinner.hidden).toBe(true);

            const change = fixture.componentInstance['change']({ add: [sea.id] });
            await settle();
            expect(spinner.hidden).toBe(false);

            first.resolve({ added: 0, removed: 0, files: [] });
            await change;
            await settle();
            expect(spinner.hidden).toBe(true);
        });

        it('keeps the parts sent before a failure applied and shown', async () => {
            const selection = files([], [], [], []);
            api.changeFileTags
                .mockImplementationOnce(async change => applyChange(current, known, change))
                .mockRejectedValueOnce(new ApiError(0, 'The server is not reachable'));
            await open(selection);

            await expect(fixture.componentInstance['change']({ add: [sea.id] })).rejects.toThrow('The server is not reachable');
            await settle();

            expect(api.changeFileTags).toHaveBeenCalledTimes(2);
            expect(chips('On some of these files')).toEqual(['Sea · 2']);
            expect(element.querySelector<HTMLElement>('.tag-loading')!.hidden).toBe(true);
        });

        it('takes a change again after a failure', async () => {
            api.changeFileTags.mockRejectedValueOnce(new ApiError(500, 'Boom'));
            await open(files([], []));

            await expect(fixture.componentInstance['change']({ add: [sea.id] })).rejects.toThrow('Boom');
            await fixture.componentInstance['change']({ add: [sea.id] });
            await settle();

            expect(chips('On all these files')).toEqual(['Sea']);
        });
    });

    it('falls back to parts of 1000 files when the server sends no limit', async () => {
        await setUp(0);
        const selection = Array.from({ length: 1001 }, (_, i) => item(`f${i}.jpg`));
        await open(selection);

        await fixture.componentInstance['change']({ add: [sea.id] });

        expect(api.changeFileTags.mock.calls.map(([change]) => change.paths.length)).toEqual([1000, 1]);
    });
});
