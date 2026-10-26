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

    describe('Find', () => {
        const type = async (text: string) => {
            const box = element.querySelector<HTMLInputElement>('[data-find]')!;
            box.value = text;
            box.dispatchEvent(new Event('input'));
            await new Promise(resolve => setTimeout(resolve, 350));
            await stable();
        };

        it('searches the whole archive by name, goes on where a page stopped, and opens the folder of a result', async () => {
            api.exploreArchive.mockResolvedValue(listing([folder(2, 'Day 1'), file(10, 'readme.txt')]));
            await show();
            api.searchArchive
                .mockResolvedValueOnce({ items: [], next: 'f10' })
                .mockResolvedValueOnce({ items: [file(11, 'summit.jpg', { where: 'Day 1', in: 2 }), file(12, 'Summit.txt', { where: '', in: 1 })], next: null });

            await type('s');
            expect(api.searchArchive).not.toHaveBeenCalled();
            await type('summ');

            expect(api.searchArchive).toHaveBeenNthCalledWith(1, 'Media/trip.zip', { q: 'summ', after: null, unlock: null }, expect.any(AbortSignal));
            expect(api.searchArchive).toHaveBeenNthCalledWith(2, 'Media/trip.zip', { q: 'summ', after: 'f10', unlock: null }, expect.any(AbortSignal));
            expect(names()).toEqual(['summit.jpg', 'Summit.txt']);
            expect([...element.querySelectorAll('[data-where]')].map(where => where.textContent!.trim())).toEqual(['in Day 1', 'in trip.zip']);
            expect(element.querySelector('[data-found]')!.textContent).toContain('2 names hold “summ”');

            api.exploreArchive.mockResolvedValue(listing([file(11, 'summit.jpg')], { folder: folder(2, 'Day 1', { files: 1 }), trail: { skipped: 0, last: [{ id: 2, name: 'Day 1' }] } }));
            element.querySelector<HTMLButtonElement>('[data-where]')!.click();
            await stable();
            expect(api.exploreArchive).toHaveBeenLastCalledWith('Media/trip.zip', { folder: 2, limit: 200, unlock: null }, expect.any(AbortSignal));
            expect(element.querySelector('[data-found]')).toBeNull();
            expect(element.querySelector<HTMLInputElement>('[data-find]')!.value).toBe('');
            expect(crumbs()).toEqual(['trip.zip', 'Day 1']);
        });

        it('looks only in the folder shown once This folder is pressed, again at once for what is typed', async () => {
            api.exploreArchive.mockResolvedValue(listing([file(11, 'summit.jpg')], { folder: folder(2, 'Day 1', { files: 1 }), trail: { skipped: 0, last: [{ id: 2, name: 'Day 1' }] } }));
            await show();
            api.searchArchive.mockResolvedValue({ items: [file(11, 'summit.jpg', { where: 'Day 1' })], next: null });
            await type('summ');
            expect(api.searchArchive).toHaveBeenLastCalledWith('Media/trip.zip', { q: 'summ', after: null, unlock: null }, expect.any(AbortSignal));

            api.searchArchive.mockResolvedValue({ items: [file(11, 'summit.jpg')], next: null });
            const here = element.querySelector<HTMLButtonElement>('[data-find-here]')!;
            here.click();
            await stable();
            expect(here.getAttribute('aria-pressed')).toBe('true');
            expect(element.querySelector<HTMLInputElement>('[data-find]')!.placeholder).toBe('Find in this folder');
            expect(api.searchArchive).toHaveBeenLastCalledWith('Media/trip.zip', { q: 'summ', folder: 2, after: null, unlock: null }, expect.any(AbortSignal));
            expect(names()).toEqual(['summit.jpg']);
            expect(element.querySelector('[data-where]')).toBeNull();
            expect(element.querySelector('[data-found]')!.textContent).toContain('1 name in this folder holds “summ”');

            api.searchArchive.mockResolvedValue({ items: [], next: null });
            await type('zebra');
            expect(element.querySelector('[data-found-none]')!.textContent).toContain('No name in this folder holds “zebra”.');
        });

        it('says when no name holds it, and Esc goes back to the folder', async () => {
            api.exploreArchive.mockResolvedValue(listing([file(10, 'readme.txt')]));
            await show();
            api.searchArchive.mockResolvedValue({ items: [], next: null });

            await type('zebra');
            expect(element.querySelector('[data-found-none]')!.textContent).toContain('No name in this archive holds “zebra”.');

            element.querySelector('[data-find]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
            await stable();
            expect(names()).toEqual(['readme.txt']);
            expect(element.querySelector('[data-counts]')).not.toBeNull();
        });
    });

    describe('Files shown in place', () => {
        const textFile = { text: 'hello from the archive\n', encoding: 'utf-8', lineEnding: 'lf', version: 'v', size: 23, modified: '2026-05-06T07:08:10', access: { overwrite: false, saveCopy: false, download: false, permanent: false }, saveLineEnding: 'lf' };
        /** Waits for the view of the entry, loaded on demand (its own chunk). */
        const until = async (ready: () => boolean) => {
            for (let i = 0; i < 100 && !ready(); i++) {
                await new Promise(resolve => setTimeout(resolve, 20));
                await stable();
            }
        };
        const open = (name: string, init: MouseEventInit = {}) => {
            const link = [...element.querySelectorAll<HTMLAnchorElement>('a.archive-open')].find(a => a.textContent!.trim() === name)!;
            const event = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ...init });
            link.dispatchEvent(event);
            return event;
        };

        it('reads a text file from the archive in the text view, read only, and goes back to the list as it was', async () => {
            api.exploreArchive.mockResolvedValue(listing([folder(2, 'Day 1'), file(10, 'readme.txt'), file(11, 'notes.md')]));
            api.archiveText.mockResolvedValue(textFile);
            await show();

            const click = open('readme.txt');
            expect(click.defaultPrevented).toBe(true);
            await until(() => Boolean(element.querySelector('[data-entry] .cm-content')));
            expect(element.querySelector('[data-entry] .archive-entry-name')!.textContent!.trim()).toBe('readme.txt');
            expect(api.archiveText).toHaveBeenCalledWith('Media/trip.zip', 10, { encoding: undefined, unlock: null }, expect.any(AbortSignal));
            expect(element.querySelector('[data-entry] .cm-content')!.textContent).toContain('hello from the archive');
            expect(element.querySelector('[data-entry] [data-follow]')).toBeNull();
            expect(element.querySelector<HTMLAnchorElement>('[data-entry] [data-download]')!.getAttribute('href'))
                .toBe(`api/files/explore/entry/${pathToken('Media/trip.zip')}/10/readme.txt?download=true`);
            expect(element.querySelector<HTMLButtonElement>('[data-previous]')!.disabled).toBe(true);
            expect(element.querySelector<HTMLButtonElement>('[data-next]')!.disabled).toBe(false);

            element.querySelector<HTMLButtonElement>('[data-back]')!.click();
            await stable();
            expect(element.querySelector('[data-entry]')).toBeNull();
            expect(element.querySelector('.archive-row.selected .archive-open')!.textContent!.trim()).toBe('readme.txt');
        });

        it('shows pictures, the arrows going from one to the next; Esc goes back; videos and Ctrl+click still open a tab', async () => {
            api.exploreArchive.mockResolvedValue(listing([file(10, 'a.jpg', { media: 'image' }), file(11, 'clip.mp4', { media: 'video' }), file(12, 'b.png', { media: 'image' })]));
            await show();

            expect(open('a.jpg', { ctrlKey: true }).defaultPrevented).toBe(false);
            expect(open('clip.mp4').defaultPrevented).toBe(false);
            await stable();
            expect(element.querySelector('[data-entry]')).toBeNull();

            open('a.jpg');
            await stable();
            const picture = () => element.querySelector<HTMLImageElement>('[data-entry-picture]')!.getAttribute('src');
            expect(picture()).toBe(`api/files/explore/entry/${pathToken('Media/trip.zip')}/10/a.jpg`);
            const entry = element.querySelector<HTMLElement>('[data-entry]')!;
            entry.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
            await stable();
            expect(picture()).toBe(`api/files/explore/entry/${pathToken('Media/trip.zip')}/12/b.png`); // the video between is passed

            element.querySelector<HTMLElement>('[data-entry]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
            await stable();
            expect(element.querySelector('[data-entry]')).toBeNull();
        });

        it('follows the links and pictures of a document to the files of the archive, Back returning to it', async () => {
            api.exploreArchive.mockResolvedValue(listing([file(10, 'guide.md')], { path: 'docs' }));
            api.archiveText.mockImplementation(async (_path: string, entry: number | string) => ({
                ...textFile, text: entry === 10 ? '# Guide\n\n![map](img/map.png)\n\n[Next part](../more/part%202.md) and [outside](../../../x.md)\n' : '# Part 2\n',
            }));
            await show();

            open('guide.md');
            await until(() => Boolean(element.querySelector('[data-entry] .markdown-content h1')));
            const token = pathToken('Media/trip.zip');
            expect(element.querySelector('[data-entry] .markdown-content img')!.getAttribute('src')).toBe(`api/files/explore/entry/${token}/at/docs/img/map.png`);
            const links = [...element.querySelectorAll<HTMLAnchorElement>('[data-entry] .markdown-content a')];
            expect(links.find(a => a.textContent === 'outside')!.hasAttribute('href'), 'out of the archive: text').toBe(false);

            links.find(a => a.textContent === 'Next part')!.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
            await until(() => element.querySelector('[data-entry] .markdown-content h1')?.textContent === 'Part 2');
            expect(api.archiveText).toHaveBeenLastCalledWith('Media/trip.zip', 'more/part 2.md', { encoding: undefined, unlock: null }, expect.any(AbortSignal));
            expect(element.querySelector('[data-entry] .archive-entry-name')!.textContent!.trim()).toBe('part 2.md');
            expect(element.querySelector<HTMLButtonElement>('[data-next]')!.disabled, 'no row of the list').toBe(true);

            element.querySelector<HTMLButtonElement>('[data-back]')!.click();
            await until(() => element.querySelector('[data-entry] .markdown-content h1')?.textContent === 'Guide');
            expect(element.querySelector('[data-entry] .archive-entry-name')!.textContent!.trim()).toBe('guide.md');
            element.querySelector<HTMLButtonElement>('[data-back]')!.click();
            await stable();
            expect(element.querySelector('[data-entry]')).toBeNull();
        });

        it('opens a protected file in place only once the password is given, sending the unlock', async () => {
            api.exploreArchive.mockResolvedValue(listing([file(10, 'secret.txt', { encrypted: true })], { protected: true }));
            api.unlockArchive.mockResolvedValue({ unlock: 'u1' });
            api.archiveText.mockResolvedValue(textFile);
            await show();
            element.querySelector<HTMLButtonElement>('button.archive-open')!.click();
            await stable();
            expect(element.querySelector('[data-password]')).not.toBeNull();

            element.querySelector<HTMLInputElement>('[data-password-box]')!.value = 'secret';
            element.querySelector<HTMLFormElement>('[data-password]')!.dispatchEvent(new Event('submit'));
            await stable();
            open('secret.txt');
            await until(() => api.archiveText.mock.calls.length > 0);
            expect(api.archiveText).toHaveBeenCalledWith('Media/trip.zip', 10, { encoding: undefined, unlock: 'u1' }, expect.any(AbortSignal));
        });
    });

    it('goes up from a folder at the top of the archive with the Up button', async () => {
        api.exploreArchive.mockResolvedValue(listing([folder(2, 'Empty')]));
        await show();
        api.exploreArchive.mockResolvedValue(listing([], { folder: folder(2, 'Empty'), trail: { skipped: 0, last: [{ id: 2, name: 'Empty' }] } }));
        element.querySelector<HTMLButtonElement>('[data-kind="folder"] .archive-open')!.click();
        await stable();
        expect(up().disabled).toBe(false);

        api.exploreArchive.mockResolvedValue(listing([folder(2, 'Empty')]));
        up().click();
        await stable();
        expect(api.exploreArchive).toHaveBeenLastCalledWith('Media/trip.zip', { folder: null, limit: 200, unlock: null }, expect.any(AbortSignal));
        expect(up().disabled).toBe(true);
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
