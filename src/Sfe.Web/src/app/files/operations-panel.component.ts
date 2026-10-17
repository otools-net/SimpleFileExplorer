import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { formatDate, formatSize, joinParts, plural } from '../core/format';
import { ClashSide, FileJobView, LibraryItem } from '../core/models';
import { baseName } from '../core/paths';
import { checkedOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { ConfigService } from '../core/config.service';
import { Rights } from '../core/rights';
import { ListingStore } from '../library/listing.store';
import { DRAG_ITEMS } from '../library/quick-links/quick-links.service';
import { FileTransfers, finished } from './file-transfers';
import { UploadsPanelComponent } from './uploads-panel.component';
import { droppedFiles, UploadQueue } from './uploads';

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
    imports: [IconComponent, UploadsPanelComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'operations-panel',
        role: 'region',
        'aria-label': 'Moves, copies and uploads',
        '[hidden]': '!transfers.jobs().length && !transfers.clipboard() && !uploads.batches().length',
        '(document:dragstart)': 'onDragStart($event)',
        '(document:dragover)': 'onDragOver($event)',
        '(document:dragleave)': 'onDragLeave($event)',
        '(document:drop)': 'onDrop($event)',
        '(document:dragend)': 'endDrag()',
    },
    template: `
        <sfe-uploads-panel />
        @if (transfers.clipboard(); as clipboard) {
            <section class="operation clipboard">
                <header class="operation-head">
                    <svg [sfeIcon]="clipboard.kind === 'move' ? 'cut' : 'copy'"></svg>
                    <span class="operation-title">{{ clipboardText() }}</span>
                    @if (transfers.canInto(clipboard.kind)) {
                        <button type="button" class="button small primary" [title]="'Put them in the folder listed (Ctrl+V)'" (click)="paste()">
                            <svg sfeIcon="paste"></svg><span>Paste here</span>
                        </button>
                    }
                    <button type="button" class="icon-button ghost small" aria-label="Forget them" title="Forget them" (click)="transfers.clipboard.set(null)">
                        <svg sfeIcon="close"></svg>
                    </button>
                </header>
            </section>
        }
        @for (job of transfers.jobs(); track job.id) {
            <section class="operation" [class.done]="isDone(job)" [attr.data-job]="job.id" aria-live="polite">
                <header class="operation-head">
                    <svg [sfeIcon]="job.kind === 'move' ? 'move' : 'copy'"></svg>
                    <span class="operation-title">{{ title(job) }}</span>
                    @if (isDone(job)) {
                        <button type="button" class="icon-button ghost small" aria-label="Close" title="Close" (click)="transfers.dismiss(job)">
                            <svg sfeIcon="close"></svg>
                        </button>
                    } @else {
                        <button type="button" class="button small" title="Stop: what is done stays, nothing half copied does" (click)="transfers.cancel(job)">Cancel</button>
                    }
                </header>
                @if (!isDone(job)) {
                    <progress [max]="progressMax(job)" [value]="progressValue(job)"></progress>
                    <p class="operation-status">{{ status(job) }}</p>
                }
                @if (job.conflict; as conflict) {
                    <div class="operation-conflict" role="group" aria-label="Name clash">
                        <p>“{{ conflict.existing.name }}” is already there.</p>
                        <dl>
                            <dt>There</dt><dd>{{ side(conflict.existing) }}</dd>
                            <dt>{{ job.kind === 'move' ? 'Moved' : 'Copied' }}</dt><dd>{{ side(conflict.source) }}</dd>
                        </dl>
                        <div class="operation-choices">
                            @if (conflict.canReplace) {
                                <button type="button" class="button small danger" title="The one there goes to the trash" (click)="answer(job, 'replace')">Replace</button>
                            }
                            <button type="button" class="button small" title="Leave this one out" (click)="answer(job, 'skip')">Skip</button>
                            <button type="button" class="button small primary" title="Keep both: this one gets a number added to its name" (click)="answer(job, 'keepBoth')">Keep both</button>
                        </div>
                        <label class="check">
                            <input type="checkbox" [checked]="forAll()" (change)="forAll.set(checked($event))">
                            <span>The same for the next ones</span>
                        </label>
                    </div>
                }
                @if (isDone(job) && problems(job).length) {
                    <ul class="operation-problems">
                        @for (result of problems(job); track result.path) {
                            <li [class.skipped]="result.status === 'skipped'">{{ result.message || baseName(result.path) }}</li>
                        }
                    </ul>
                }
            </section>
        }
    `,
})
export class OperationsPanelComponent implements OnInit {
    protected readonly transfers = inject(FileTransfers);
    protected readonly uploads = inject(UploadQueue);
    private readonly store = inject(ListingStore);
    private readonly config = inject(ConfigService);

    protected readonly forAll = signal(false);
    protected readonly baseName = baseName;
    /** The folder under the pointer during a drag. */
    private over: HTMLElement | null = null;

    ngOnInit(): void {
        this.transfers.resume();
        void this.uploads.resume();
    }

    protected clipboardText(): string {
        const clipboard = this.transfers.clipboard();
        if (!clipboard)
            return '';
        const what = clipboard.items.length === 1 ? `“${clipboard.items[0].name}”` : plural(clipboard.items.length, 'item');
        return `${what} ${clipboard.kind === 'move' ? 'cut' : 'copied'}: open a folder to paste ${clipboard.items.length === 1 ? 'it' : 'them'} into`;
    }

    protected paste(): void {
        this.transfers.paste(this.store.route().path);
    }

    protected isDone(job: FileJobView): boolean {
        return finished(job);
    }

    protected title(job: FileJobView): string {
        const where = `“${baseName(job.target) || 'the library'}”`;
        const count = plural(job.total, 'item');
        if (job.state === 'cancelled')
            return `Stopped: ${job.done} of ${count} ${job.kind === 'move' ? 'moved' : 'copied'} to ${where}`;
        if (job.state === 'completed') {
            const done = job.results.filter(result => result.status === 'done').length;
            return `${plural(done, 'item')} ${job.kind === 'move' ? 'moved' : 'copied'} to ${where}`;
        }
        return `${job.kind === 'move' ? 'Moving' : 'Copying'} ${count} to ${where}`;
    }

    protected progressMax(job: FileJobView): number {
        return job.kind === 'copy' && job.bytesTotal ? job.bytesTotal : Math.max(1, job.total);
    }

    protected progressValue(job: FileJobView): number {
        return job.kind === 'copy' && job.bytesTotal ? job.bytesDone : job.done;
    }

    protected status(job: FileJobView): string {
        if (job.state === 'waiting')
            return 'Waiting for your answer…';
        return joinParts([
            `${job.done} of ${job.total}`,
            job.kind === 'copy' && job.bytesTotal ? `${formatSize(job.bytesDone)} of ${formatSize(job.bytesTotal)}` : null,
            job.current ? baseName(job.current) : null,
        ]);
    }

    protected side(side: ClashSide): string {
        return joinParts([side.isFolder ? 'folder' : formatSize(side.size), side.modified ? `modified ${formatDate(side.modified)}` : null], ', ');
    }

    /** What was not done, skipped included. */
    protected problems(job: FileJobView) {
        return job.results.filter(result => result.status !== 'done');
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
        const element = event.target instanceof Element ? event.target.closest<HTMLElement>('sfe-listing [data-path]') : null;
        if (!element || !event.dataTransfer)
            return;
        const path = element.getAttribute('data-path')!;
        const paths = this.store.isSelected(path) ? this.store.selectedItems().map(item => item.path) : [path];
        if (!event.dataTransfer.getData(DRAG_ITEMS))
            event.dataTransfer.setData(DRAG_ITEMS, JSON.stringify(paths));
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
        if (!target || !event.dataTransfer?.types.includes(DRAG_ITEMS))
            return;
        const copy = event.ctrlKey || event.metaKey;
        if (!this.allowed(target, copy ? 'copy' : 'move'))
            return;
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
            this.endDrag();
            if (place && event.dataTransfer) {
                event.preventDefault();
                // The items are read at once: the browser empties them once the event is over.
                void droppedFiles(event.dataTransfer).then(files => this.uploads.add(files, place.path));
            }
            return;
        }
        const target = this.dropTarget(event);
        const data = event.dataTransfer?.getData(DRAG_ITEMS);
        this.endDrag();
        if (!target || !data)
            return;
        const kind = event.ctrlKey || event.metaKey ? 'copy' : 'move';
        if (!this.allowed(target, kind))
            return;
        const into = target.getAttribute('data-drop')!;
        const paths = parsePaths(data).filter(path => path !== into && !into.startsWith(`${path}/`));
        if (!paths.length)
            return;
        event.preventDefault();
        void this.transfers.transfer(kind, paths, into);
    }

    protected endDrag(): void {
        this.highlight(null);
    }

    /** Files dragged from the computer (not items of the page). */
    private isUpload(event: DragEvent): boolean {
        const types = event.dataTransfer?.types ?? [];
        return types.includes('Files') && !types.includes(DRAG_ITEMS);
    }

    /** Where dropped files go: a folder of the page under the pointer, else the folder listed (anywhere on the listing). */
    private uploadPlace(event: DragEvent): { path: string; element: HTMLElement | null } | null {
        const target = this.dropTarget(event);
        if (target) {
            const path = target.getAttribute('data-drop')!;
            const folder = this.store.items().find(item => item.path === path && item.kind === 'directory');
            return (folder ? this.uploads.canUploadInto(folder) : this.config.can(Rights.uploadFiles)) ? { path, element: target } : null;
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
        const folder: LibraryItem | undefined = this.store.items().find(item => item.path === path && item.kind === 'directory');
        return folder ? this.transfers.canInto(kind, folder) : this.transfers.allows(kind);
    }

    private highlight(target: HTMLElement | null): void {
        if (this.over === target)
            return;
        this.over?.classList.remove('drop-over');
        target?.classList.add('drop-over');
        this.over = target;
    }
}

function parsePaths(json: string): string[] {
    try {
        const value: unknown = JSON.parse(json);
        return Array.isArray(value) ? value.filter((path): path is string => typeof path === 'string') : [];
    } catch {
        return [];
    }
}
