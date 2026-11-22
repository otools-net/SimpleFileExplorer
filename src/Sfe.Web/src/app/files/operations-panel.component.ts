import {
    ChangeDetectionStrategy,
    Component,
    DestroyRef,
    effect,
    ElementRef,
    inject,
    OnInit,
    signal,
} from '@angular/core';
import { ClashSide, LibraryItem } from '../core/models';
import { FileJobView } from './file.models';
import { baseName } from '../core/paths';
import { checkedOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { FolderMenu } from '../library/folder-menu';
import { ContextMenuDirective, MenuEntry, MenuTarget } from '../menus/context-menu';
import { ConfigService } from '../core/config.service';
import { Rights } from '../core/rights';
import { ListingStore } from '../library/listing.store';
import { DRAG_ITEMS } from '../library/quick-links/quick-links.service';
import { FileTransfers, finished, pasteTitle } from './file-transfers';
import { UploadsPanelComponent } from './uploads-panel.component';
import { droppedFiles, UploadQueue } from './uploads';
import { uploadConnection } from './upload-places';
import { dragItemsData, parseDragItems } from './drag-items';
import { canRetryTransfer, jobPlaces, PlaceTransfers } from './place-transfers';
import { Api } from '../core/api.service';
import {
    clashSide,
    clipboardText,
    compressTitle,
    destination,
    extractTitle,
    jobStatus,
    timeLeft,
    transferTitle,
    whereOf,
} from './operations-texts';

/** Drop targets: listed folders and the folders of the location bar (`data-drop` holding their path). */
const DROP_TARGET = '[data-drop]';

/**
 * The moves and copies of the session, at the bottom of the page while they run and until closed: progress, Cancel,
 * the question of a name clash (with "for the rest"), then what was done and what was not. It also takes the drops of
 * listed items onto folders: a move, a copy with Ctrl (the drag starts in the quick links, see QuickLinksComponent);
 * and of files and folders from the computer: uploaded into the folder dropped on, else the folder listed.
 */
@Component({
    selector: 'sfe-operations-panel',
    imports: [IconComponent, UploadsPanelComponent, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'operations-panel',
        role: 'region',
        '[attr.aria-label]': 'label',
        '[hidden]': '!transfers.jobs().length && !transfers.clipboard() && !uploads.batches().length',
        '(document:dragstart)': 'onDragStart($event)',
        '(document:dragover)': 'onDragOver($event)',
        '(document:dragleave)': 'onDragLeave($event)',
        '(document:drop)': 'onDrop($event)',
        '(document:dragend)': 'endDrag()',
    },
    templateUrl: './operations-panel.component.html',
})
export class OperationsPanelComponent implements OnInit {
    protected readonly transfers = inject(FileTransfers);
    protected readonly uploads = inject(UploadQueue);
    private readonly store = inject(ListingStore);
    private readonly config = inject(ConfigService);
    private readonly folders = inject(FolderMenu);
    private readonly places = inject(PlaceTransfers);
    private readonly api = inject(Api);

    protected readonly label = $localize`Operations and uploads`;
    protected readonly forAll = signal(false);
    protected readonly baseName = baseName;
    protected readonly pasteTitle = pasteTitle;
    protected readonly closeLabel = $localize`Close`;
    protected readonly closeHint = $localize`Take it off the list.`;
    protected readonly cancelHint = $localize`Stop it. What is already done stays.`;
    protected readonly canRetry = canRetryTransfer;
    /** When each job's bytes were first seen moving, and how many then: its speed since. */
    private readonly moving = new Map<number, { at: number; bytes: number }>();
    /** The folder under the pointer during a drag. */
    private over: HTMLElement | null = null;

    constructor() {
        // Toasts show above the panel (styles/toasts.css): how far its top is from the window's bottom, 0 while hidden.
        if (typeof ResizeObserver === 'undefined') return;
        const host = inject(ElementRef<HTMLElement>).nativeElement;
        const root = document.documentElement.style;
        const place = () => {
            const rect = host.getBoundingClientRect();
            root.setProperty(
                '--operations-clearance',
                `${rect.height > 0 ? Math.max(0, Math.round(window.innerHeight - rect.top)) : 0}px`,
            );
        };
        const observer = new ResizeObserver(place);
        observer.observe(host);
        window.addEventListener('resize', place);
        // The selection bar lifts the panel without resizing it.
        effect(() => {
            this.store.selection();
            requestAnimationFrame(place);
        });
        inject(DestroyRef).onDestroy(() => {
            observer.disconnect();
            window.removeEventListener('resize', place);
            root.removeProperty('--operations-clearance');
        });
    }

    ngOnInit(): void {
        this.transfers.resume();
        void this.uploads.resume();
    }

    protected clipboardText(): string {
        const clipboard = this.transfers.clipboard();
        return clipboard ? clipboardText(clipboard) : '';
    }

    protected paste(): void {
        this.transfers.paste(this.store.route().path);
    }

    /** A job's menu (a right click): its button (Cancel, or Close once done), then the folder it puts things into. */
    protected jobMenu(job: FileJobView): MenuTarget {
        return {
            kind: 'job',
            key: `job:${job.id}`,
            name: this.title(job),
            entries: () => {
                const entries: MenuEntry[] = this.isDone(job)
                    ? [
                          {
                              id: 'close',
                              label: this.closeLabel,
                              icon: 'close',
                              title: this.closeHint,
                              run: () => this.transfers.dismiss(job),
                          },
                      ]
                    : [
                          {
                              id: 'cancel',
                              label: $localize`Cancel`,
                              icon: 'close',
                              title: this.cancelHint,
                              run: () => void this.transfers.cancel(job),
                          },
                      ];
                return [...entries, ...this.folders.targetEntries(job.target)];
            },
        };
    }

    protected isDone(job: FileJobView): boolean {
        return finished(job);
    }

    protected title(job: FileJobView): string {
        const where = whereOf(job.target, this.places.nameOf(jobPlaces(job).to));
        if (job.kind === 'compress') return compressTitle(job, where);
        if (job.kind === 'extract') return extractTitle(job, where);
        return transferTitle(job, destination(where, this.places.journey(job)));
    }

    protected progressMax(job: FileJobView): number {
        return job.kind !== 'move' && job.bytesTotal ? job.bytesTotal : Math.max(1, job.total);
    }

    protected progressValue(job: FileJobView): number {
        return job.kind !== 'move' && job.bytesTotal ? job.bytesDone : job.done;
    }

    protected status(job: FileJobView): string {
        return jobStatus(job, job.kind === 'extract' || job.kind === 'compress' ? this.left(job) : null);
    }

    /** The time left of a job measured in bytes, from when its bytes started moving (a queue's wait left out). */
    private left(job: FileJobView): string | null {
        if (job.state !== 'running' || !job.bytesDone) return null;
        let start = this.moving.get(job.id);
        if (!start) this.moving.set(job.id, (start = { at: Date.now(), bytes: job.bytesDone }));
        return timeLeft(start, Date.now(), job.bytesDone, job.bytesTotal);
    }

    protected side(side: ClashSide): string {
        return clashSide(side);
    }

    /** What was not done, skipped included; an extraction's result whatever it is (what it left out). */
    protected problems(job: FileJobView) {
        return job.results.filter(
            (result) =>
                result.status !== 'done' || ((job.kind === 'extract' || job.kind === 'compress') && result.message),
        );
    }

    /** Runs a transfer's failed items again, following the job answered (a new one takes the old one's place). */
    protected async retry(job: FileJobView): Promise<void> {
        const view = await this.transfers.follow(() => this.api.retryFileJob(job.id), 'retry');
        if (view && view.id !== job.id) this.transfers.dismiss(job);
    }

    protected answer(job: FileJobView, choice: 'replace' | 'skip' | 'keepBoth'): void {
        void this.transfers.answer(job, choice, this.forAll());
        this.forAll.set(false);
    }

    protected checked(event: Event): boolean {
        return checkedOf(event);
    }

    /* ---------- Dropping listed items onto folders ---------- */

    /** Dragged items may be moved as well as copied (the quick links set the data). */
    protected onDragStart(event: DragEvent): void {
        const element =
            event.target instanceof Element ? event.target.closest<HTMLElement>('sfe-listing [data-path]') : null;
        if (!element || !event.dataTransfer) return;
        const path = element.getAttribute('data-path')!;
        const paths = this.store.isSelected(path) ? this.store.selectedItems().map((item) => item.path) : [path];
        if (!event.dataTransfer.getData(DRAG_ITEMS))
            event.dataTransfer.setData(DRAG_ITEMS, dragItemsData(paths, this.places.here()));
        event.dataTransfer.effectAllowed = 'all';
    }

    protected onDragOver(event: DragEvent): void {
        if (this.isUpload(event)) {
            const place = this.uploadPlace(event);
            this.highlight(place?.element ?? null);
            if (place) {
                event.preventDefault();
                event.dataTransfer!.dropEffect = 'copy';
            }
            return;
        }
        const target = this.dropTarget(event);
        this.highlight(target);
        if (!target || !event.dataTransfer?.types.includes(DRAG_ITEMS)) return;
        const copy = event.ctrlKey || event.metaKey;
        if (!this.allowed(target, copy ? 'copy' : 'move')) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = copy ? 'copy' : 'move';
    }

    protected onDragLeave(event: DragEvent): void {
        if (this.over && !(event.relatedTarget instanceof Node && this.over.contains(event.relatedTarget)))
            this.highlight(null);
    }

    protected onDrop(event: DragEvent): void {
        if (this.isUpload(event)) {
            const place = this.uploadPlace(event);
            const connection = uploadConnection(this.config);
            this.endDrag();
            if (place && event.dataTransfer) {
                event.preventDefault();
                // The items are read at once: the browser empties them once the event is over. They go to the place
                // listed now (the library's or a connection's), even if the listing changes meanwhile.
                void droppedFiles(event.dataTransfer).then((files) => this.uploads.add(files, place.path, connection));
            }
            return;
        }
        const target = this.dropTarget(event);
        const data = event.dataTransfer?.getData(DRAG_ITEMS);
        this.endDrag();
        if (!target || !data) return;
        const kind = event.ctrlKey || event.metaKey ? 'copy' : 'move';
        if (!this.allowed(target, kind)) return;
        const into = target.getAttribute('data-drop')!;
        const dragged = parseDragItems(data);
        const places = { from: dragged.connection, to: this.places.here() };
        // Within one place, a folder can't go into itself (nor where it is).
        const paths = dragged.paths.filter(
            (path) => places.from !== places.to || (path !== into && !into.startsWith(`${path}/`)),
        );
        if (!paths.length) return;
        event.preventDefault();
        void this.transfers.transfer(kind, paths, into, places);
    }

    protected endDrag(): void {
        this.highlight(null);
    }

    /** Files dragged from the computer (not items of the page). */
    private isUpload(event: DragEvent): boolean {
        const types = event.dataTransfer?.types ?? [];
        return types.includes('Files') && !types.includes(DRAG_ITEMS);
    }

    /**
     * Where dropped files go: a folder of the page under the pointer, else the folder listed (anywhere on the listing).
     */
    private uploadPlace(event: DragEvent): { path: string; element: HTMLElement | null } | null {
        const target = this.dropTarget(event);
        if (target) {
            const path = target.getAttribute('data-drop')!;
            const folder = this.store.items().find((item) => item.path === path && item.kind === 'directory');
            return (folder ? this.uploads.canUploadInto(folder) : this.config.can(Rights.uploadFiles))
                ? { path, element: target }
                : null;
        }
        const page = event.target instanceof Element ? event.target.closest<HTMLElement>('main.page') : null;
        return page && this.uploads.canUploadInto() ? { path: this.store.route().path, element: page } : null;
    }

    private dropTarget(event: DragEvent): HTMLElement | null {
        return event.target instanceof Element ? event.target.closest<HTMLElement>(DROP_TARGET) : null;
    }

    /**
     * The session may drop into `target`: listed folders by their access, the folders of the location bar by the
     * space's rights (the server checks again).
     */
    private allowed(target: HTMLElement, kind: 'move' | 'copy'): boolean {
        const path = target.getAttribute('data-drop')!;
        const folder: LibraryItem | undefined = this.store
            .items()
            .find((item) => item.path === path && item.kind === 'directory');
        return folder ? this.transfers.canInto(kind, folder) : this.transfers.allows(kind);
    }

    private highlight(target: HTMLElement | null): void {
        if (this.over === target) return;
        this.over?.classList.remove('drop-over');
        target?.classList.add('drop-over');
        this.over = target;
    }
}
