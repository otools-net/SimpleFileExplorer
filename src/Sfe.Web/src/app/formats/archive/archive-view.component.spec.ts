import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../../core/api.service';
import { ArchiveItem, ArchiveListing } from '../../core/models';
import { pathToken } from '../../core/paths';
import { Rights } from '../../core/rights';
import { ApiMock, item, loadConfig, mockApi, settle, testProviders } from '../../../testing/fakes';
import { Dialogs } from '../../library/dialogs.service';
import { ARCHIVE_FORMAT, FILE_VIEW_HOST } from '../formats';
import { ArchiveViewComponent } from './archive-view.component';

const folder = (id: number, name: string, counts: Partial<ArchiveItem> = {}): ArchiveItem =>
    ({ kind: 'folder', id, name, size: 0, packed: 0, modified: null, encrypted: false, folders: 0, files: 0, allFiles: 0, ...counts });
const file = (id: number, name: string, overrides: Partial<ArchiveItem> = {}): ArchiveItem =>
    ({ kind: 'file', id, name, size: 2048, packed: 1024, modified: '2026-05-06T07:08:10', encrypted: false, ...overrides });

function listing(items: ArchiveItem[], overrides: Partial<ArchiveListing> = {}): ArchiveListing {
    const folders = items.filter(entry => entry.kind === 'folder').length;
    return {
        format: 'zip', complete: true, stopped: null, needsPassword: false, entries: items.length, leftOut: 0,
        folder: folder(1, '', { folders, files: items.length - folders, allFiles: items.length - folders, size: 4096 }),
        trail: { skipped: 0, last: [] }, items, next: null, ...overrides,
    };
}

describe('ArchiveViewComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<ArchiveViewComponent>;
    let element: HTMLElement;
    let rights: Set<string>;

    const names = () => [...element.querySelectorAll('.archive-body .archive-row:not(.placeholder) .archive-open')].map(name => name.textContent!.trim());
    const crumbs = () => [...element.querySelectorAll('.archive-crumb')].map(crumb => crumb.textContent!.trim());
    const up = () => element.querySelector<HTMLButtonElement>('[data-up]')!;

    async function stable(): Promise<void> {
        await settle();
        await fixture.whenStable();
        await settle();
    }

    async function show(path = 'Media/trip.zip'): Promise<void> {
        fixture = TestBed.createComponent(ArchiveViewComponent);
        element = fixture.nativeElement;
        fixture.componentRef.setInput('item', item(path, { kind: 'file' }));
        fixture.componentRef.setInput('format', ARCHIVE_FORMAT);
        await stable();
    }

    beforeEach(async () => {
        api = mockApi();
        rights = new Set([Rights.viewFiles, Rights.download]);
        TestBed.configureTestingModule({ providers: testProviders(api, { provide: FILE_VIEW_HOST, useValue: { place: 'viewer', can: (right: string) => rights.has(right) } }) });
        await loadConfig(api);
    });

    afterEach(() => {
        fixture?.destroy();
        TestBed.resetTestingModule();
    });

    it('shows the top of the archive: its folders, then its files, with their sizes and the totals', async () => {
        api.exploreArchive.mockResolvedValue(listing([folder(2, 'Day 1', { folders: 0, files: 3, allFiles: 3, size: 6 }), file(10, 'readme.txt')]));
        await show();

        expect(api.exploreArchive).toHaveBeenCalledWith('Media/trip.zip', { limit: 200, unlock: null }, expect.any(AbortSignal));
        expect(names()).toEqual(['Day 1', 'readme.txt']);
        expect(element.querySelector('[data-kind="folder"] .archive-size')!.textContent!.trim()).toBe('3 files');
        expect(element.querySelector('[data-kind="file"] .archive-size')!.textContent!.trim()).toBe('2 KB');
        expect(element.querySelector('[data-counts]')!.textContent).toContain('ZIP');
        expect(crumbs()).toEqual(['trip.zip']);
        expect(up().disabled).toBe(true);
        expect(element.hasAttribute('data-owns-keys')).toBe(true);
    });

    it('says the list is being read, then shows it', async () => {
        api.exploreArchive.mockResolvedValueOnce({ read: 340000 }).mockResolvedValue(listing([file(10, 'a.txt')]));
        await show();
        expect(element.querySelector('[data-reading]')!.textContent).toContain('340,000 so far');

        await new Promise(resolve => setTimeout(resolve, 700));
        await stable();
        expect(element.querySelector('[data-reading]')).toBeNull();
        expect(names()).toEqual(['a.txt']);
    });

    it('opens a folder, shows the way to it collapsed when deep, and goes back up', async () => {
        api.exploreArchive.mockResolvedValue(listing([folder(2, 'Day 1')]));
        await show();
        api.exploreArchive.mockResolvedValue(listing([file(11, 'b.txt')], {
            folder: folder(7, 'deep', { files: 1 }),
            trail: { skipped: 4996, last: [{ id: 5, name: 'c' }, { id: 6, name: 'd' }, { id: 7, name: 'deep' }] },
        }));

        element.querySelector<HTMLButtonElement>('[data-kind="folder"] .archive-open')!.click();
        await stable();
        expect(api.exploreArchive).toHaveBeenLastCalledWith('Media/trip.zip', { folder: 2, limit: 200, unlock: null }, expect.any(AbortSignal));
        expect(crumbs()).toEqual(['trip.zip', '…', 'c', 'd', 'deep']);
        expect(element.querySelector('[data-skipped]')!.getAttribute('title')).toBe('4,996 folders in between');

        element.querySelector('.archive-scroller')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', bubbles: true }));
        await stable();
        expect(api.exploreArchive).toHaveBeenLastCalledWith('Media/trip.zip', { folder: 6, limit: 200, unlock: null }, expect.any(AbortSignal));
    });

    it('goes to a folder by its path inside the archive', async () => {
        api.exploreArchive.mockResolvedValue(listing([]));
        await show();
        const box = element.querySelector<HTMLInputElement>('[data-go]')!;
        box.value = '/Day 2/Deep/';
        box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
        await stable();

        expect(api.exploreArchive).toHaveBeenLastCalledWith('Media/trip.zip', { at: 'Day 2/Deep', limit: 200, unlock: null }, expect.any(AbortSignal));
        expect(element.querySelector('[data-empty]')).not.toBeNull();
    });

    it('opens files in a tab, from the app, with the right to download; links and protected entries are only listed', async () => {
        api.exploreArchive.mockResolvedValue(listing([file(10, 'photo #1.jpg'), file(11, 'secret.txt', { encrypted: true }), file(12, 'passwd', { kind: 'link' })]));
        await show();

        const link = element.querySelector<HTMLAnchorElement>('[data-kind="file"] a.archive-open')!;
        expect(link.getAttribute('href')).toBe(`api/files/explore/entry/${pathToken('Media/trip.zip')}/10/photo%20%231.jpg`);
        expect(link.target).toBe('_blank');
        expect(element.querySelectorAll('a.archive-open')).toHaveLength(1);
        expect(element.querySelector('.archive-lock')).not.toBeNull();
        expect(element.querySelector('[data-kind="link"] .archive-open')!.getAttribute('title')).toContain('never created');

        const view = fixture.componentInstance as unknown as { menuOf(entry: ArchiveItem): { entries(): { id: string; href?: string }[] } | null };
        const entries = view.menuOf(file(10, 'photo #1.jpg'))!.entries();
        expect(entries.map(entry => entry.id)).toEqual(['newTab', 'download']);
        expect(entries[1].href).toMatch(/\?download=true$/);
        expect(view.menuOf(file(12, 'passwd', { kind: 'link' }))).toBeNull();
    });

    it('extracts the folder shown, the whole archive at its top, or one entry, through the Extract dialog', async () => {
        const open = vi.fn();
        TestBed.inject(Dialogs).extract.set({ open } as never);
        api.exploreArchive.mockResolvedValue(listing([folder(2, 'Day 1'), file(10, 'readme.txt')]));
        await show();

        element.querySelector<HTMLButtonElement>('[data-extract-shown]')!.click();
        expect(open).toHaveBeenLastCalledWith({ path: 'Media/trip.zip', name: 'trip.zip', unlock: null, folders: undefined, what: undefined });

        const view = fixture.componentInstance as unknown as { menuOf(entry: ArchiveItem): { entries(): { id: string; run?: () => void }[] } | null };
        view.menuOf(file(10, 'readme.txt'))!.entries().find(entry => entry.id === 'extract')!.run!();
        expect(open).toHaveBeenLastCalledWith({ path: 'Media/trip.zip', name: 'trip.zip', unlock: null, what: '“readme.txt”', folders: undefined, files: [10] });

        api.exploreArchive.mockResolvedValue(listing([file(11, 'a.txt')], { folder: folder(2, 'Day 1', { files: 1 }), trail: { skipped: 0, last: [{ id: 2, name: 'Day 1' }] } }));
        element.querySelector<HTMLButtonElement>('[data-kind="folder"] .archive-open')!.click();
        await stable();
        element.querySelector<HTMLButtonElement>('[data-extract-shown]')!.click();
        expect(open).toHaveBeenLastCalledWith({ path: 'Media/trip.zip', name: 'trip.zip', unlock: null, folders: [2], what: '“Day 1”' });

        rights.delete(Rights.download);
        await show();
        expect(element.querySelector('[data-extract-shown]'), 'its content is taken: the right to download').toBeNull();
    });

    it('offers no file without the right to download', async () => {
        rights.delete(Rights.download);
        api.exploreArchive.mockResolvedValue(listing([file(10, 'a.txt')]));
        await show();

        expect(element.querySelector('a.archive-open')).toBeNull();
        expect(names()).toEqual(['a.txt']);
    });

    it('says why it can not be explored, with the archive to download', async () => {
        api.exploreArchive.mockResolvedValue({ type: 'not-explorable', detail: 'This file can\'t be explored: extract it instead.' });
        await show('Media/fake.zip');

        expect(element.querySelector('[data-failed]')!.textContent).toContain('extract it instead');
        expect(element.querySelector('[data-failed] a')).not.toBeNull();
        expect(element.querySelector<HTMLElement>('.archive-scroller')!.hidden).toBe(true);
    });

    it('says when the list stopped, what was left out, and when a password protects it', async () => {
        api.exploreArchive.mockResolvedValue(listing([file(10, 'a.txt')], { complete: false, stopped: 'The archive is cut off.', leftOut: 3 }));
        await show();
        expect(element.querySelector('[data-stopped]')!.textContent).toContain('The archive is cut off.');
        expect(element.querySelector('[data-left-out]')!.textContent).toContain('3 entries are not listed');
        fixture.destroy();

        api.exploreArchive.mockResolvedValue(listing([], { complete: false, stopped: 'The archive is protected by a password.', needsPassword: true }));
        await show();
        expect(element.querySelector('[data-password]')).not.toBeNull();
        expect(element.querySelector('[data-stopped]')).toBeNull();
        // Its list isn't read yet: not an empty folder.
        expect(element.querySelector('[data-empty]')).toBeNull();
        expect(element.querySelector('[data-counts]')).toBeNull();
    });

    async function typePassword(password: string): Promise<void> {
        const box = element.querySelector<HTMLInputElement>('[data-password-box]')!;
        box.value = password;
        element.querySelector<HTMLFormElement>('[data-password]')!.dispatchEvent(new Event('submit', { cancelable: true }));
        await stable();
    }

    it('asks the password of an archive whose list is encrypted, then lists it with the unlock', async () => {
        api.exploreArchive.mockResolvedValueOnce(listing([], { format: '7z', complete: false, stopped: 'The archive is protected by a password.', needsPassword: true }));
        await show('Media/hidden.7z');
        expect(element.querySelector('[data-password]')).not.toBeNull();
        expect(element.querySelector('[data-password]')!.textContent).not.toContain('names stay readable');
        expect(element.querySelector('[data-stopped]')).toBeNull();

        api.unlockArchive.mockResolvedValue({ unlock: 'u'.repeat(64) });
        api.exploreArchive.mockResolvedValue(listing([file(10, 'readme.txt')], { format: '7z', protected: true, unlocked: true }));
        await typePassword('secret');

        expect(api.unlockArchive).toHaveBeenCalledWith('Media/hidden.7z', 'secret');
        expect(api.exploreArchive).toHaveBeenLastCalledWith('Media/hidden.7z', { folder: null, limit: 200, unlock: 'u'.repeat(64) }, expect.any(AbortSignal));
        expect(element.querySelector('[data-password]')).toBeNull();
        expect(names()).toEqual(['readme.txt']);
        expect(element.querySelector<HTMLAnchorElement>('a.archive-open')!.getAttribute('href')).toContain(`?unlock=${'u'.repeat(64)}`);
    });

    it('says when the password is wrong, or tried too often', async () => {
        api.exploreArchive.mockResolvedValue(listing([], { needsPassword: true, complete: false }));
        await show('Media/hidden.zip');

        api.unlockArchive.mockRejectedValueOnce(new ApiError(422, "That password doesn't open this archive.", null, 'wrong-password'));
        await typePassword('guess');
        expect(element.querySelector('[data-password-problem]')!.textContent).toContain("doesn't open");

        api.unlockArchive.mockRejectedValueOnce(new ApiError(429, 'Too Many Requests'));
        await typePassword('guess again');
        expect(element.querySelector('[data-password-problem]')!.textContent).toContain('wait a minute');
    });

    it('opens protected files once the password is given', async () => {
        api.exploreArchive.mockResolvedValue(listing([file(10, 'secret.txt', { encrypted: true }), file(11, 'plain.txt')], { protected: true }));
        await show('Media/secret.zip');
        expect(element.querySelector('[data-password]')).toBeNull();
        expect(element.querySelector('[data-protected]')).not.toBeNull();
        expect([...element.querySelectorAll('a.archive-open')].map(link => link.textContent!.trim())).toEqual(['plain.txt']);

        element.querySelector<HTMLButtonElement>('[data-kind="file"] button.archive-open')!.click();
        await stable();
        expect(element.querySelector('[data-password]')!.textContent).toContain('names stay readable');

        api.unlockArchive.mockResolvedValue({ unlock: 'k'.repeat(64) });
        api.exploreArchive.mockResolvedValue(listing([file(10, 'secret.txt', { encrypted: true }), file(11, 'plain.txt')], { protected: true, unlocked: true }));
        await typePassword('secret');
        expect([...element.querySelectorAll('a.archive-open')].map(link => link.textContent!.trim())).toEqual(['secret.txt', 'plain.txt']);
        expect(element.querySelector('[data-protected]')).toBeNull();
    });

    it('loads the next page as the rows near the end of those loaded', async () => {
        const first = Array.from({ length: 50 }, (_, i) => file(100 + i, `f${String(i).padStart(3, '0')}.txt`));
        api.exploreArchive.mockResolvedValueOnce(listing(first, { next: 'cursor-1', folder: folder(1, '', { files: 120 }) }))
            .mockResolvedValue(listing([file(999, 'more.txt')], { next: null }));
        await show();

        expect(api.exploreArchive).toHaveBeenLastCalledWith('Media/trip.zip', { folder: 1, after: 'cursor-1', limit: 1000, unlock: null }, expect.any(AbortSignal));
        expect(api.exploreArchive).toHaveBeenCalledTimes(2);
        expect(element.querySelector('.archive-body')!.getAttribute('style')).toContain(`height: ${120 * 32}px`);
    });
});
