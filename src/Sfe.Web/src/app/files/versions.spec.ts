import { TestBed } from '@angular/core/testing';
import { LibraryItem } from '../core/models';
import { ToastService } from '../core/toast.service';
import {
    ApiMock,
    buttonNamed,
    fakeConfig,
    item,
    loadConfig,
    mockApi,
    settle,
    testProviders,
} from '../../testing/fakes';
import { Dialogs } from '../library/dialogs.service';
import { FileVersions, TrashItem } from './file.models';
import { PropertiesDialog } from './properties.dialog';
import { VersionsDialog } from './versions.dialog';

let api: ApiMock;

const notes = item('Docs/notes.txt', { size: 12 });

function version(id: number, deletedAt: string): TrashItem {
    return {
        id,
        path: notes.path,
        name: notes.name,
        isFolder: false,
        size: 2048,
        files: null,
        deletedAt,
        deletedBy: 'Sam',
        deletedById: 2,
        previousVersion: true,
    };
}

function versions(overrides: Partial<FileVersions> = {}): FileVersions {
    return {
        items: [version(8, '2026-11-23T10:00:00Z'), version(5, '2026-11-20T10:00:00Z')],
        restoreOver: true,
        restoreCopy: true,
        download: true,
        ...overrides,
    };
}

async function open(answer = versions()) {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api, fakeConfig());
    api.fileVersions.mockResolvedValue(answer);
    const fixture = TestBed.createComponent(VersionsDialog);
    await fixture.whenStable();
    fixture.componentInstance.open(notes);
    await settle();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
}

afterEach(() => vi.restoreAllMocks());

describe('VersionsDialog', () => {
    it("lists a file's versions newest first, each opened from its own address", async () => {
        const { element } = await open();

        expect(api.fileVersions).toHaveBeenCalledWith('Docs/notes.txt');
        const rows = [...element.querySelectorAll<HTMLElement>('.versions-row')];
        expect(rows.map((row) => row.dataset['id'])).toEqual(['8', '5']);
        expect(rows[0].textContent).toContain('Saved over by Sam.');
        const link = rows[0].querySelector<HTMLAnchorElement>('a')!;
        expect(link.getAttribute('href')).toBe('api/v1/files/versions/8/content');
        expect(link.target).toBe('_blank');
    });

    it('says so when there is none, and offers only what the rights allow', async () => {
        const { element } = await open(versions({ items: [] }));
        expect(element.textContent).toContain('No previous versions');

        const { element: limited } = await reopen(versions({ restoreOver: false }));
        expect(buttonNamed(limited, 'Restore a copy')).toBeDefined();
        expect(buttonNamed(limited, 'Restore')).toBeUndefined();
    });

    it('restores a copy next to the file, then lists again', async () => {
        const { fixture, element } = await open();
        const toast = vi.spyOn(TestBed.inject(ToastService), 'success');
        api.restoreVersionCopy.mockResolvedValue(item('Docs/notes (2).txt') satisfies LibraryItem);

        buttonNamed(element, 'Restore a copy')!.click();
        await settle();
        fixture.detectChanges();

        expect(api.restoreVersionCopy).toHaveBeenCalledWith(8);
        expect(toast).toHaveBeenCalledWith('The version is restored as “notes (2).txt”.');
        expect(api.fileVersions).toHaveBeenCalledTimes(2);
    });

    it('asks before restoring over the file, and tells why it was refused', async () => {
        const { fixture, element } = await open();
        const ask = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
        const error = vi.spyOn(TestBed.inject(ToastService), 'error');
        api.restoreVersion.mockResolvedValue({ type: 'scan-running', detail: 'A scan is running.' });

        buttonNamed(element, 'Restore')!.click();
        await settle();
        expect(api.restoreVersion).not.toHaveBeenCalled();

        buttonNamed(element, 'Restore')!.click();
        await settle();
        fixture.detectChanges();
        expect(ask).toHaveBeenCalledTimes(2);
        expect(api.restoreVersion).toHaveBeenCalledWith(8);
        expect(error).toHaveBeenCalledWith('Could not restore the version: A scan is running.');
    });

    it("gives Properties a line counting a file's versions, which opens them", async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api, fakeConfig());
        const opened = vi.fn();
        TestBed.inject(Dialogs).versions.set({ open: opened } as never);
        api.properties.mockResolvedValue({
            item: notes,
            details: {
                path: notes.path,
                name: notes.name,
                isFolder: false,
                size: 12,
                created: null,
                modified: '2026-11-23T10:00:00Z',
                accessed: null,
                hidden: false,
                readOnly: false,
                isLink: false,
            },
            canChecksum: false,
        });
        api.fileVersions.mockResolvedValue(versions());
        const fixture = TestBed.createComponent(PropertiesDialog);
        await fixture.whenStable();
        await fixture.componentInstance.open(notes);
        await settle();
        fixture.detectChanges();
        const element = fixture.nativeElement as HTMLElement;

        expect(element.querySelector('.properties-versions')!.textContent).toContain('2 versions');
        buttonNamed(element, 'Show…')!.click();
        expect(opened).toHaveBeenCalledWith(notes);
    });
});

async function reopen(answer: FileVersions) {
    TestBed.resetTestingModule();
    return open(answer);
}
