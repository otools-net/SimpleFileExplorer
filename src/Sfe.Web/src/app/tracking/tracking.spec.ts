import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { DuplicateGroup, DuplicateResults, MissingFileInfo, MissingFileResults, SimilarVideoResults } from '../core/models';
import { ToastService } from '../core/toast.service';
import { ApiMock, deferred, fileTags, item, loadConfig, mockApi, settle, summary, tag, testProviders } from '../../testing/fakes';
import { DuplicatesPanelComponent } from './duplicates-panel.component';
import { MissingPanelComponent } from './missing-panel.component';
import { SimilarPanelComponent } from './similar-panel.component';

let api: ApiMock;
let toasts: ToastService;

beforeEach(async () => {
    localStorage.clear();
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api);
    toasts = TestBed.inject(ToastService);
});

afterEach(() => vi.restoreAllMocks());

const buttonNamed = (root: HTMLElement, label: string) => [...root.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent!.trim() === label);
const statusText = (root: HTMLElement) => root.querySelector('.settings-status-text')!.textContent!;

describe('DuplicatesPanelComponent', () => {
    let fixture: ComponentFixture<DuplicatesPanelComponent>;
    let element: HTMLElement;
    let changed: number;

    const MB = 1024 * 1024;
    const sun = summary(tag('Sun'));
    const group = (size: number, ...paths: string[]): DuplicateGroup => ({
        size, fingerprint: `f${size}`, count: paths.length, wastedBytes: size * (paths.length - 1),
        files: paths.map(path => item(path, { size, tags: path.includes('tagged') ? [sun] : [] })),
    });
    const results = (groups: DuplicateGroup[], overrides: Partial<DuplicateResults> = {}): DuplicateResults => ({
        path: '', groups, hasMore: false, next: null, fingerprinting: true, minimumSize: 0, canRemove: true,
        summary: { groups: groups.length, files: groups.reduce((n, g) => n + g.count, 0), wastedBytes: groups.reduce((n, g) => n + g.wastedBytes, 0), unfingerprinted: 0 },
        ...overrides,
    });
    const groupElements = () => [...element.querySelectorAll<HTMLElement>('section.tracking-group')];

    async function create(answer: DuplicateResults, folder = ''): Promise<void> {
        api.duplicates.mockResolvedValue(answer);
        fixture = TestBed.createComponent(DuplicatesPanelComponent);
        fixture.componentRef.setInput('folder', folder);
        element = fixture.nativeElement;
        changed = 0;
        fixture.componentInstance.changed.subscribe(() => changed++);
        await fixture.componentInstance.load();
        await settle();
    }

    it('sums up the groups found and what could be freed', async () => {
        await create(results([group(2 * MB, 'a/x.jpg', 'b/x.jpg', 'c/x.jpg'), group(MB, 'a/y.jpg', 'b/y.jpg')]));

        expect(statusText(element)).toBe('2 groups of identical files in the library: 5 MB could be freed');
        expect(groupElements().map(g => [...g.querySelectorAll('header > *')].map(part => part.textContent)))
            .toEqual([['3 copies', '2 MB each · 4 MB extra'], ['2 copies', '1 MB each · 1 MB extra']]);
        expect(fixture.componentInstance.badge()).toBe('2');
    });

    it('says when there are none, and why some files were not compared', async () => {
        await create(results([], { minimumSize: MB, fingerprinting: false, summary: { groups: 0, files: 0, wastedBytes: 0, unfingerprinted: 3 } }));

        expect(statusText(element)).toBe('No duplicates in the library.');
        const detail = element.querySelector('sfe-status-box .field-hint')!.textContent!;
        expect(detail).toContain('files under 1 MB are left out');
        expect(detail).toContain('3 indexed files without a fingerprint can\'t be compared');
        expect(detail).toContain('scans don\'t fingerprint files');
        expect(fixture.componentInstance.badge()).toBe('');
    });

    it('reports a search that failed', async () => {
        api.duplicates.mockRejectedValue(new ApiError(500, 'Boom'));
        fixture = TestBed.createComponent(DuplicatesPanelComponent);
        element = fixture.nativeElement;
        await fixture.componentInstance.load();
        await settle();

        expect(statusText(element)).toBe('Could not look for duplicates: Boom');
    });

    it('looks only in the folder browsed when asked, remembering it', async () => {
        await create(results([]), 'Media');
        expect(api.duplicates).toHaveBeenLastCalledWith({ path: '', after: null });

        element.querySelector<HTMLInputElement>('.tracking-scope input')!.click();
        await settle();

        expect(api.duplicates).toHaveBeenLastCalledWith({ path: 'Media', after: null });
        expect(localStorage.getItem('sfe.duplicates.folderOnly')).toBe('1');
    });

    it('has no folder choice at the root', async () => {
        await create(results([]));

        expect(element.querySelector('.tracking-scope')).toBeNull();
    });

    it('loads the next page after the last group', async () => {
        await create(results([group(MB, 'a', 'b')], { hasMore: true, next: 'cursor-1' }));
        api.duplicates.mockResolvedValue(results([group(MB / 2, 'c', 'd')]));

        buttonNamed(element, 'Load more')!.click();
        await settle();

        expect(api.duplicates).toHaveBeenLastCalledWith({ path: '', after: 'cursor-1' });
        expect(groupElements()).toHaveLength(2);
        expect(buttonNamed(element, 'Load more')!.hidden).toBe(true);
    });

    it('offers no removal when the server does not allow it', async () => {
        await create(results([group(MB, 'a/x.jpg', 'b/x.jpg')], { canRemove: false }));

        expect(buttonNamed(element, 'Remove')).toBeUndefined();
    });

    it('asks which copy to keep, the first other one by default', async () => {
        await create(results([group(MB, 'a/x.jpg', 'b/x.jpg', 'c/x.jpg')]));

        groupElements()[0].querySelectorAll<HTMLButtonElement>('button.danger')[1].click();
        await settle();

        const keep = element.querySelector<HTMLSelectElement>('.tracking-keep')!;
        expect([...keep.options].map(o => o.value)).toEqual(['a/x.jpg', 'c/x.jpg']);
        expect(keep.value).toBe('a/x.jpg');
        expect(element.querySelector('.tracking-confirm')!.textContent).toContain('Delete this file from the disk and keep');
    });

    it('names the only other copy, and says where the tags go', async () => {
        await create(results([group(MB, 'a/tagged.jpg', 'b/x.jpg')]));

        groupElements()[0].querySelector<HTMLButtonElement>('button.danger')!.click();
        await settle();

        const confirm = element.querySelector('.tracking-confirm')!;
        expect(confirm.querySelector('strong')!.textContent).toBe('b/x.jpg');
        expect(confirm.textContent).toContain('add its tags to');
    });

    it('cancels the removal without deleting anything', async () => {
        await create(results([group(MB, 'a/x.jpg', 'b/x.jpg')]));
        groupElements()[0].querySelector<HTMLButtonElement>('button.danger')!.click();
        await settle();

        buttonNamed(element, 'Cancel')!.click();
        await settle();

        expect(element.querySelector('.tracking-confirm')).toBeNull();
        expect(api.removeDuplicate).not.toHaveBeenCalled();
    });

    it('deletes a copy keeping the chosen one, which gets the tags', async () => {
        const show = vi.spyOn(toasts, 'show');
        await create(results([group(MB, 'a/tagged.jpg', 'b/x.jpg', 'c/x.jpg'), group(MB, 'd', 'e')]));
        api.removeDuplicate.mockResolvedValue({ removed: 'a/tagged.jpg', kept: fileTags('c/x.jpg', [sun]) });

        groupElements()[0].querySelector<HTMLButtonElement>('button.danger')!.click();
        await settle();
        const keep = element.querySelector<HTMLSelectElement>('.tracking-keep')!;
        keep.value = 'c/x.jpg';
        keep.dispatchEvent(new Event('change'));
        buttonNamed(element, 'Delete this copy')!.click();
        await settle();

        expect(api.removeDuplicate).toHaveBeenCalledWith('a/tagged.jpg', 'c/x.jpg');
        expect(show).toHaveBeenCalledWith('“tagged.jpg” deleted; “c/x.jpg” is kept with its tags.');
        const rows = [...groupElements()[0].querySelectorAll('li.tracking-row')];
        expect(rows.map(r => r.querySelector('.tracking-name')!.textContent)).toEqual(['x.jpg', 'x.jpg']);
        expect(rows[1].querySelector('.tag-name')!.textContent!.trim()).toBe('Sun');
        expect(groupElements()[0].querySelector('header strong')!.textContent).toBe('2 copies');
        expect(statusText(element)).toBe('2 groups of identical files in the library: 2 MB could be freed');
        expect(changed).toBe(1);
    });

    it('drops a group once a single copy is left', async () => {
        await create(results([group(MB, 'a/x.jpg', 'b/x.jpg'), group(2 * MB, 'c', 'd')]));
        api.removeDuplicate.mockResolvedValue({ removed: 'a/x.jpg', kept: fileTags('b/x.jpg') });

        groupElements()[0].querySelector<HTMLButtonElement>('button.danger')!.click();
        await settle();
        buttonNamed(element, 'Delete this copy')!.click();
        await settle();

        expect(groupElements()).toHaveLength(1);
        expect(statusText(element)).toBe('1 group of identical files in the library: 2 MB could be freed');
        expect(fixture.componentInstance.badge()).toBe('1');
    });

    it('keeps the copy listed when the server refuses to delete it', async () => {
        const error = vi.spyOn(toasts, 'error');
        await create(results([group(MB, 'a/x.jpg', 'b/x.jpg')]));
        api.removeDuplicate.mockRejectedValue(new ApiError(409, 'The files are no longer identical.'));

        groupElements()[0].querySelector<HTMLButtonElement>('button.danger')!.click();
        await settle();
        buttonNamed(element, 'Delete this copy')!.click();
        await settle();

        expect(error).toHaveBeenCalledWith('Could not remove “x.jpg”: The files are no longer identical.', 8000);
        expect(groupElements()[0].querySelectorAll('li.tracking-row')).toHaveLength(2);
        expect(changed).toBe(0);
    });

    it('disables the delete button while deleting', async () => {
        const pending = deferred<never>();
        await create(results([group(MB, 'a/x.jpg', 'b/x.jpg')]));
        api.removeDuplicate.mockReturnValue(pending.promise);

        groupElements()[0].querySelector<HTMLButtonElement>('button.danger')!.click();
        await settle();
        buttonNamed(element, 'Delete this copy')!.click();
        await settle();

        const button = element.querySelector<HTMLButtonElement>('.tracking-confirm button.danger')!;
        expect(button.disabled).toBe(true);
        expect(button.getAttribute('aria-busy')).toBe('true');
    });

    it('drops a page of a search started before the last one', async () => {
        const slow = deferred<DuplicateResults>();
        await create(results([]));
        api.duplicates.mockReturnValueOnce(slow.promise).mockResolvedValueOnce(results([group(MB, 'new/a', 'new/b')]));

        const first = fixture.componentInstance.load();
        await fixture.componentInstance.load();
        slow.resolve(results([group(MB, 'old/a', 'old/b')]));
        await first;
        await settle();

        expect(element.querySelector('[title="Open new/a"]')).not.toBeNull();
        expect(element.querySelector('[title="Open old/a"]')).toBeNull();
    });
});

describe('MissingPanelComponent', () => {
    let fixture: ComponentFixture<MissingPanelComponent>;
    let element: HTMLElement;
    let changed: number;

    const missing = (id: number, path: string, candidates: string[] = []): MissingFileInfo => ({
        id, path, name: path.split('/').pop()!, size: 2048, modified: '2026-01-01T00:00:00Z', missingSince: '2026-02-01T00:00:00Z',
        tags: [summary(tag('Kept'))], candidates: candidates.map(c => item(c)),
    });
    const results = (items: MissingFileInfo[], overrides: Partial<MissingFileResults> = {}): MissingFileResults =>
        ({ items, total: items.length, hasMore: false, next: null, enabled: true, ...overrides });
    const rows = () => [...element.querySelectorAll<HTMLElement>('li.tracking-missing')];

    async function create(answer: MissingFileResults): Promise<void> {
        api.missingFiles.mockResolvedValue(answer);
        fixture = TestBed.createComponent(MissingPanelComponent);
        element = fixture.nativeElement;
        changed = 0;
        fixture.componentInstance.changed.subscribe(() => changed++);
        await fixture.componentInstance.load();
        await settle();
    }

    it('lists the missing files with where they were and their possible new places', async () => {
        await create(results([missing(1, 'Old/a.jpg', ['New/a.jpg', 'Other/a.jpg']), missing(2, 'root.jpg')]));

        expect(statusText(element)).toBe('2 tagged files missing');
        expect(rows()[0].querySelector('.tracking-was')!.textContent).toMatch(/^Was in Old · 2 KB · missing since /);
        expect(rows()[1].querySelector('.tracking-was')!.textContent).toMatch(/^Was in the library root/);
        expect(rows()[0].querySelector('.tracking-sub')!.textContent).toBe('Possible new places');
        expect(rows()[1].querySelector('.tracking-sub')!.textContent).toContain('No identical file is indexed');
        expect(fixture.componentInstance.badge()).toBe('2');
    });

    it('says when nothing is missing, with nothing to relink or forget', async () => {
        await create(results([]));

        expect(statusText(element)).toBe('No tagged file is missing.');
        expect(buttonNamed(element, 'Relink now')!.disabled).toBe(true);
        expect(buttonNamed(element, 'Forget all')!.disabled).toBe(true);
    });

    it('says when keeping the tags of moved files is off', async () => {
        await create(results([missing(1, 'a.jpg')], { enabled: false }));

        expect(element.querySelector('sfe-status-box')!.getAttribute('data-state')).toBe('unavailable');
        expect(buttonNamed(element, 'Relink now')!.disabled).toBe(true);
        expect(buttonNamed(element, 'Forget all')!.disabled).toBe(false);
    });

    it('relinks a file to a candidate', async () => {
        const show = vi.spyOn(toasts, 'show');
        await create(results([missing(1, 'Old/a.jpg', ['New/a.jpg']), missing(2, 'b.jpg')]));
        api.relinkMissingFile.mockResolvedValue(undefined);

        buttonNamed(rows()[0], 'Relink here')!.click();
        await settle();

        expect(api.relinkMissingFile).toHaveBeenCalledWith(1, 'New/a.jpg');
        expect(show).toHaveBeenCalledWith('Tags of “a.jpg” moved to “New/a.jpg”.');
        expect(rows()).toHaveLength(1);
        expect(statusText(element)).toBe('1 tagged file missing');
        expect(changed).toBe(1);
    });

    it('relinks to a file typed by hand, from the library root', async () => {
        vi.spyOn(window, 'prompt').mockReturnValue('  /Elsewhere/a.jpg ');
        await create(results([missing(1, 'Old/a.jpg')]));
        api.relinkMissingFile.mockResolvedValue(undefined);

        buttonNamed(rows()[0], 'Choose file…')!.click();
        await settle();

        expect(api.relinkMissingFile).toHaveBeenCalledWith(1, 'Elsewhere/a.jpg');
    });

    it('does nothing when the typed path is cancelled or blank', async () => {
        const ask = vi.spyOn(window, 'prompt').mockReturnValueOnce(null).mockReturnValueOnce('   ');
        await create(results([missing(1, 'Old/a.jpg')]));

        buttonNamed(rows()[0], 'Choose file…')!.click();
        buttonNamed(rows()[0], 'Choose file…')!.click();
        await settle();

        expect(ask).toHaveBeenCalledWith('Give the tags of “a.jpg” to which file? Path from the library root:', 'Old/a.jpg');
        expect(api.relinkMissingFile).not.toHaveBeenCalled();
    });

    it('drops a file another tab already relinked or forgot', async () => {
        const error = vi.spyOn(toasts, 'error');
        await create(results([missing(1, 'a.jpg', ['b.jpg'])]));
        api.relinkMissingFile.mockRejectedValue(new ApiError(404, 'Not Found'));

        buttonNamed(rows()[0], 'Relink here')!.click();
        await settle();

        expect(error).toHaveBeenCalledWith('This file was already relinked or forgotten.');
        expect(rows()).toHaveLength(0);
    });

    it('keeps a file whose relink failed otherwise', async () => {
        const error = vi.spyOn(toasts, 'error');
        await create(results([missing(1, 'a.jpg', ['b.jpg'])]));
        api.relinkMissingFile.mockRejectedValue(new ApiError(400, '“b.jpg” is not a file of the library.'));

        buttonNamed(rows()[0], 'Relink here')!.click();
        await settle();

        expect(error).toHaveBeenCalledWith('Could not relink: “b.jpg” is not a file of the library.');
        expect(rows()).toHaveLength(1);
        expect(buttonNamed(rows()[0], 'Relink here')!.disabled).toBe(false);
    });

    it('disables every action of the list while one runs', async () => {
        await create(results([missing(1, 'a.jpg', ['b.jpg']), missing(2, 'c.jpg')]));
        api.relinkMissingFile.mockReturnValue(new Promise(() => { }));

        buttonNamed(rows()[0], 'Relink here')!.click();
        await settle();

        expect(rows().flatMap(row => [...row.querySelectorAll<HTMLButtonElement>('.tracking-actions button')]).every(b => b.disabled)).toBe(true);
    });

    it('forgets a file after asking, also when it was gone already', async () => {
        vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValue(true);
        await create(results([missing(1, 'a.jpg'), missing(2, 'b.jpg')]));
        api.forgetMissingFiles.mockResolvedValueOnce(undefined).mockRejectedValueOnce(new ApiError(404, 'Not Found'));

        buttonNamed(rows()[0], 'Forget')!.click();
        await settle();
        expect(api.forgetMissingFiles).not.toHaveBeenCalled();

        buttonNamed(rows()[0], 'Forget')!.click();
        await settle();
        buttonNamed(rows()[0], 'Forget')!.click();
        await settle();

        expect(api.forgetMissingFiles.mock.calls).toEqual([[1], [2]]);
        expect(rows()).toHaveLength(0);
        expect(statusText(element)).toBe('No tagged file is missing.');
    });

    it('keeps a file that could not be forgotten', async () => {
        vi.spyOn(window, 'confirm').mockReturnValue(true);
        const error = vi.spyOn(toasts, 'error');
        await create(results([missing(1, 'a.jpg')]));
        api.forgetMissingFiles.mockRejectedValue(new ApiError(500, 'Boom'));

        buttonNamed(rows()[0], 'Forget')!.click();
        await settle();

        expect(error).toHaveBeenCalledWith('Could not forget it: Boom');
        expect(rows()).toHaveLength(1);
    });

    it('relinks every file with a single likely place, then reloads', async () => {
        const show = vi.spyOn(toasts, 'show');
        await create(results([missing(1, 'a.jpg', ['b.jpg']), missing(2, 'c.jpg')]));
        api.relinkMissingFiles.mockResolvedValueOnce({ relinked: 1, forgotten: 0, remaining: 1 }).mockResolvedValueOnce({ relinked: 0, forgotten: 0, remaining: 1 });
        api.missingFiles.mockResolvedValue(results([missing(2, 'c.jpg')]));

        buttonNamed(element, 'Relink now')!.click();
        await settle();
        expect(show).toHaveBeenLastCalledWith('1 file relinked; 1 file still missing.');
        expect(rows()).toHaveLength(1);
        expect(changed).toBe(1);

        buttonNamed(element, 'Relink now')!.click();
        await settle();
        expect(show).toHaveBeenLastCalledWith('No missing file has a single likely new place.');
        expect(changed).toBe(1);
    });

    it('forgets them all after asking with how many', async () => {
        const ask = vi.spyOn(window, 'confirm').mockReturnValue(true);
        await create(results([missing(1, 'a.jpg'), missing(2, 'b.jpg')], { total: 1234 }));
        api.forgetMissingFiles.mockResolvedValue(undefined);
        api.missingFiles.mockResolvedValue(results([]));

        buttonNamed(element, 'Forget all')!.click();
        await settle();

        expect(ask).toHaveBeenCalledWith(`Forget all ${(1234).toLocaleString()} missing files and their tags?`);
        expect(api.forgetMissingFiles).toHaveBeenCalledWith();
        expect(rows()).toHaveLength(0);
    });

    it('pages with the id cursor', async () => {
        await create(results([missing(1, 'a.jpg')], { hasMore: true, next: 1, total: 2 }));
        api.missingFiles.mockResolvedValue(results([missing(2, 'b.jpg')], { total: null }));

        buttonNamed(element, 'Load more')!.click();
        await settle();

        expect(api.missingFiles).toHaveBeenLastCalledWith({ after: 1 });
        expect(rows()).toHaveLength(2);
        // The total of the first page stays.
        expect(statusText(element)).toBe('2 tagged files missing');
    });

    it('reports a list that could not be read', async () => {
        api.missingFiles.mockRejectedValue(new ApiError(500, 'Boom'));
        fixture = TestBed.createComponent(MissingPanelComponent);
        element = fixture.nativeElement;
        await fixture.componentInstance.load();
        await settle();

        expect(statusText(element)).toBe('Could not read the missing files: Boom');
    });
});

describe('SimilarPanelComponent', () => {
    let fixture: ComponentFixture<SimilarPanelComponent>;
    let element: HTMLElement;

    const run = (state: SimilarVideoResults['status']['state'], overrides: Partial<SimilarVideoResults['status']> = {}) =>
        ({ state, checked: 0, total: 0, started: null, finished: null, message: null, ...overrides });
    const results = (overrides: Partial<SimilarVideoResults> = {}): SimilarVideoResults => ({
        path: '', status: run('completed', { finished: '2026-10-10T10:00:00Z' }), summary: { groups: 1, files: 2, wastedBytes: 1024, videos: 10, compared: 8, minimumPercent: 80 },
        groups: [{ count: 3, wastedBytes: 1024, videos: [{ file: item('a.mp4'), match: null, duration: 65 }, { file: item('b.mp4'), match: 92, duration: 64 }] }],
        hasMore: false, next: null, trickplayEnabled: true, ...overrides,
    });

    async function create(answer: SimilarVideoResults, open = true): Promise<void> {
        api.similarVideos.mockResolvedValue(answer);
        fixture = TestBed.createComponent(SimilarPanelComponent);
        fixture.componentRef.setInput('isOpen', open);
        element = fixture.nativeElement;
        await fixture.componentInstance.load();
        // No settle(): it waits on a timer, which some tests fake.
        fixture.detectChanges();
    }

    afterEach(() => vi.useRealTimers());

    it('sums up the last comparison', async () => {
        await create(results());

        expect(statusText(element)).toBe('1 group of similar videos in the library: 1 KB more than the biggest of each');
        const detail = element.querySelector('sfe-status-box .field-hint')!.textContent!;
        expect(detail).toContain('8 of 10 indexed videos compared (the others have no trickplay yet)');
        expect(detail).toContain('listed from 80% match');
        expect(element.querySelector('.tracking-more')!.textContent).toContain('1 more video');
        expect(fixture.componentInstance.badge()).toBe('1');
    });

    it('says when nothing was compared yet, or the comparison failed', async () => {
        await create(results({ status: run('idle'), summary: null, groups: [] }));
        expect(statusText(element)).toBe('The videos have not been compared since SFE started.');

        await create(results({ status: run('failed', { message: 'ffmpeg not found' }), summary: null, groups: [] }));
        expect(statusText(element)).toBe('The comparison failed: ffmpeg not found');
    });

    it('mentions trickplay being off', async () => {
        await create(results({ trickplayEnabled: false }));

        expect(element.querySelector('sfe-status-box .field-hint')!.textContent).toContain('trickplay is off');
    });

    it('follows a running comparison while shown, keeping the list until it is over', async () => {
        vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
        try {
            await create(results({ status: run('running', { checked: 3, total: 10 }) }));
            expect(statusText(element)).toBe('Comparing videos… 3 of 10 videos read');

            api.similarVideos.mockResolvedValue(results({ status: run('running', { checked: 7, total: 10 }), groups: [] }));
            await vi.advanceTimersByTimeAsync(3000);
            fixture.detectChanges();
            expect(statusText(element)).toBe('Comparing videos… 7 of 10 videos read');
            expect(element.querySelectorAll('section.tracking-group').length).toBe(1);

            api.similarVideos.mockResolvedValue(results());
            await vi.advanceTimersByTimeAsync(3000);
            fixture.detectChanges();
            expect(statusText(element)).toContain('1 group of similar videos');
        } finally {
            vi.useRealTimers();
        }
    });

    it('does not poll while hidden', async () => {
        vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
        try {
            await create(results({ status: run('running') }), false);
            await vi.advanceTimersByTimeAsync(10000);

            expect(api.similarVideos).toHaveBeenCalledTimes(1);
        } finally {
            vi.useRealTimers();
        }
    });

    it('starts a comparison, reporting a refusal', async () => {
        const error = vi.spyOn(toasts, 'error');
        await create(results());
        api.compareVideos.mockRejectedValue(new ApiError(500, 'Boom'));

        await fixture.componentInstance['compare']();

        expect(api.compareVideos).toHaveBeenCalled();
        expect(error).toHaveBeenCalledWith('Could not compare the videos: Boom');
        expect(api.similarVideos).toHaveBeenCalledTimes(2);
    });

    it('starts over when a next page belongs to an older comparison', async () => {
        const error = vi.spyOn(toasts, 'error');
        await create(results({ hasMore: true, next: '1:10' }));
        api.similarVideos.mockRejectedValueOnce(new ApiError(404, 'Gone')).mockResolvedValue(results());

        await fixture.componentInstance.load({ append: true });
        await settle();

        expect(error).toHaveBeenCalledWith('Could not load more: Gone');
        expect(api.similarVideos).toHaveBeenLastCalledWith({ path: '', after: null });
    });
});
