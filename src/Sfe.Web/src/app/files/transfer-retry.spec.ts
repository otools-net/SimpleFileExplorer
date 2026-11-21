import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api.service';
import { DirectoryListing } from '../core/models';
import { ToastService } from '../core/toast.service';
import { ApiMock, loadConfig, mockApi, settle, testProviders } from '../../testing/fakes';
import { ListingStore } from '../library/listing.store';
import { SidebarService } from '../library/sidebar/sidebar.service';
import { TASK_NAMES, TASK_PURPOSES } from '../tasks/tasks';
import { FileJobView } from './file.models';
import { FileTransfers } from './file-transfers';
import { OperationsPanelComponent } from './operations-panel.component';
import { canRetryTransfer } from './place-transfers';

let api: ApiMock;

const failed = { path: 'Trips/b.jpg', newPath: null, status: 'failed', message: 'b.jpg: the connection timed out' };
const skipped = { path: 'Trips/c.jpg', newPath: null, status: 'skipped' };
const done = { path: 'Trips/a.jpg', newPath: 'Media/a.jpg', status: 'done' };

function job(overrides: Partial<FileJobView> = {}): FileJobView {
    return {
        id: 1,
        kind: 'copy',
        state: 'completed',
        target: 'Media',
        total: 2,
        done: 2,
        bytesTotal: 0,
        bytesDone: 0,
        current: null,
        conflict: null,
        results: [done, failed],
        started: '2026-11-14T10:00:00Z',
        finished: '2026-11-14T10:01:00Z',
        fromConnection: 7,
        toConnection: null,
        ...overrides,
    };
}

const listing: DirectoryListing = {
    path: 'Media',
    name: 'Media',
    parentPath: '',
    items: [],
    offset: 0,
    limit: 100,
    total: 0,
    directoryCount: 0,
    fileCount: 0,
};

beforeEach(async () => {
    api = mockApi();
    TestBed.configureTestingModule({ providers: testProviders(api) });
    await loadConfig(api);
    vi.spyOn(TestBed.inject(SidebarService), 'refresh').mockImplementation(() => {});
    api.siblings.mockResolvedValue({ path: 'Media', previous: null, next: null, position: 1, count: 1 });
    api.browse.mockResolvedValue(listing);
    const store = TestBed.inject(ListingStore);
    store.route.set({ path: 'Media', q: '', sort: '', desc: false });
    await store.load();
});

afterEach(() => vi.restoreAllMocks());

let fixture: ComponentFixture<OperationsPanelComponent>;

/** The panel following `jobs` while they run, then as they are (the panel keeps the ended jobs it followed). */
async function panel(jobs: FileJobView[]): Promise<HTMLElement> {
    api.fileJobs.mockResolvedValue(jobs.map((each) => ({ ...each, state: 'running', finished: null })));
    fixture = TestBed.createComponent(OperationsPanelComponent);
    fixture.detectChanges();
    await settle();
    api.fileJobs.mockResolvedValue(jobs);
    api.cancelFileJob.mockResolvedValue(undefined);
    await TestBed.inject(FileTransfers).cancel(jobs[0]);
    await settle();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
}

function retryButton(element: HTMLElement, id: number): HTMLButtonElement | null {
    return element.querySelector<HTMLButtonElement>(`[data-job="${id}"] [data-retry]`);
}

describe('Retrying a transfer', () => {
    it('is offered for ended transfers with failed items only', () => {
        expect(canRetryTransfer(job())).toBe(true);
        expect(canRetryTransfer(job({ state: 'cancelled', fromConnection: null, toConnection: 8 }))).toBe(true);
        expect(canRetryTransfer(job({ fromConnection: null }))).toBe(false);
        expect(canRetryTransfer(job({ results: [done, skipped] }))).toBe(false);
        expect(canRetryTransfer(job({ state: 'running', finished: null }))).toBe(false);
    });

    it('shows Retry on those jobs in the panel, not on library jobs nor running ones', async () => {
        const element = await panel([
            job(),
            job({ id: 2, fromConnection: null }),
            job({ id: 3, results: [done, skipped] }),
            job({ id: 4, state: 'running', finished: null }),
        ]);

        expect(retryButton(element, 1)?.textContent?.trim()).toBe('Retry');
        expect(retryButton(element, 2)).toBeNull();
        expect(retryButton(element, 3)).toBeNull();
        expect(retryButton(element, 4)).toBeNull();
    });

    it('calls the retry route and follows the job answered in place of the old one', async () => {
        const element = await panel([job()]);
        const again = job({ id: 9, state: 'running', finished: null, done: 0, total: 1, results: [] });
        api.retryFileJob.mockResolvedValue(again);
        api.fileJobs.mockResolvedValue([again, job()]);

        retryButton(element, 1)!.click();
        await settle();
        fixture.detectChanges();

        expect(api.retryFileJob).toHaveBeenCalledWith(1);
        expect(element.querySelector('[data-job="9"]')).not.toBeNull();
        expect(element.querySelector('[data-job="1"]')).toBeNull();
    });

    it('shows the problem of a refused retry (409) in a toast', async () => {
        const element = await panel([job()]);
        const error = vi.spyOn(TestBed.inject(ToastService), 'error');
        api.retryFileJob.mockRejectedValue(new ApiError(409, 'Nothing to retry: every item was copied.'));

        retryButton(element, 1)!.click();
        await settle();

        expect(error).toHaveBeenCalledWith(expect.stringContaining('Nothing to retry: every item was copied.'));
        expect(element.querySelector('[data-job="1"]')).not.toBeNull();
    });
});

describe('The transfer task kind', () => {
    it('has a name and a purpose in the task history', () => {
        expect(TASK_NAMES.transfer).toBe('Transfer');
        expect(TASK_PURPOSES.transfer).toBe('Copies and moves items between the library and connections.');
    });
});
