import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal, viewChild, WritableSignal } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { copyText, CopyPathService } from '../core/copy-path.service';
import { formatDate, formatDuration, formatResolution, formatSize, joinParts, plural } from '../core/format';
import { ChecksumAlgorithm, FolderTotals, ItemProperties, LibraryItem, PropertyJob } from '../core/models';
import { parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { registerDialog } from '../library/dialogs.service';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from '../tags/tag-chip.component';

/** How often (ms) a running count or checksum is asked for. */
const POLL = 400;

const ALGORITHMS: [ChecksumAlgorithm, string][] = [['sha256', 'SHA-256'], ['sha1', 'SHA-1'], ['md5', 'MD5']];

/**
 * Properties of a file or folder (Alt+Enter, or its menu): what the disk says (size, dates, attributes), what the index
 * knows (resolution, duration, tags; a folder's totals), and on demand a folder counted on the disk or a file's
 * checksum, which run on the server and stop when the sheet closes.
 */
@Component({
    selector: 'sfe-properties-dialog',
    imports: [SheetComponent, TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Properties" [subtitle]="properties()?.item?.name ?? item()?.name ?? null" variant="properties-dialog" (closed)="closed()">
            @if (error(); as error) {
                <p class="form-error">{{ error }}</p>
            } @else if (properties(); as p) {
                <dl class="properties">
                    <dt>Name</dt><dd class="properties-name">{{ p.item.name }}</dd>
                    <dt>Kind</dt><dd>{{ kind() }}</dd>
                    <dt>In</dt><dd>{{ folder() }}</dd>
                    @if (!p.details.isFolder) {
                        <dt>Size</dt><dd>{{ size(p.details.size) }}</dd>
                    }
                    <dt>Modified</dt><dd>{{ date(p.details.modified) }}</dd>
                    @if (p.details.created) {
                        <dt>Created</dt><dd>{{ date(p.details.created) }}</dd>
                    }
                    @if (p.details.accessed) {
                        <dt>Opened</dt><dd>{{ date(p.details.accessed) }}</dd>
                    }
                    @if (attributes()) {
                        <dt>Attributes</dt><dd>{{ attributes() }}</dd>
                    }
                    @if (media()) {
                        <dt>Media</dt><dd>{{ media() }}</dd>
                    }
                    @if (!p.details.isFolder && p.item.indexed === false) {
                        <dt>Index</dt><dd>Not indexed yet: no scan has seen it</dd>
                    }
                    @if (showTags() && p.item.tags?.length) {
                        <dt>Tags</dt>
                        <dd class="properties-tags">
                            @for (tag of p.item.tags; track tag.id) {
                                <sfe-tag-chip [tag]="tag" />
                            }
                        </dd>
                    }
                </dl>

                @if (p.details.isFolder) {
                    <section class="properties-section" aria-label="What the folder holds">
                        <h3>What it holds</h3>
                        <p class="properties-totals">{{ totalsText() }}</p>
                        @if (count(); as job) {
                            <p class="properties-count" role="status">{{ countText() }}</p>
                        }
                        <div class="properties-actions">
                            @if (counting()) {
                                <button type="button" class="button small" title="Stop counting" (click)="stop(count())">Stop</button>
                            } @else {
                                <button type="button" class="button small count-button" title="Go through the folder on the server's disk: exact now, but takes a while for large folders"
                                        (click)="startCount()">Count on disk</button>
                            }
                        </div>
                    </section>
                } @else if (p.canChecksum) {
                    <section class="properties-section" aria-label="Checksum">
                        <h3>Checksum</h3>
                        <div class="properties-actions">
                            <label class="visually-hidden" for="checksum-algorithm">Algorithm</label>
                            <select id="checksum-algorithm" [value]="algorithm()" (change)="algorithm.set($any($event.target).value)" [disabled]="summing()">
                                @for (option of algorithms; track option[0]) {
                                    <option [value]="option[0]">{{ option[1] }}</option>
                                }
                            </select>
                            @if (summing()) {
                                <button type="button" class="button small" title="Stop reading the file" (click)="stop(checksum())">Stop</button>
                            } @else {
                                <button type="button" class="button small checksum-button" title="Read the whole file on the server for its checksum" (click)="startChecksum()">Compute</button>
                            }
                        </div>
                        @if (checksum(); as job) {
                            @if (job.checksum) {
                                <p class="properties-checksum"><code>{{ job.checksum }}</code>
                                    <button type="button" class="button small ghost" title="Copy the checksum" (click)="copy(job.checksum)">Copy</button></p>
                            } @else {
                                <p role="status">{{ checksumText() }}</p>
                            }
                        }
                    </section>
                }
            } @else {
                <p role="status">Loading…</p>
            }
            <span sheetFooter class="spacer"></span>
            @if (copyPaths.enabled() && item()) {
                <button sheetFooter type="button" class="button" title="Copy where it is on the server" (click)="copyPaths.copy(item()!.path)">Copy full path</button>
            }
            <button sheetFooter type="button" class="button primary" (click)="sheet.close()">Close</button>
        </sfe-sheet>
    `,
})
export class PropertiesDialog {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly copyPaths = inject(CopyPathService);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly algorithms = ALGORITHMS;
    protected readonly item = signal<LibraryItem | null>(null);
    protected readonly properties = signal<ItemProperties | null>(null);
    protected readonly totals = signal<FolderTotals | null>(null);
    protected readonly error = signal<string | null>(null);
    protected readonly count = signal<PropertyJob | null>(null);
    protected readonly checksum = signal<PropertyJob | null>(null);
    protected readonly algorithm = signal<ChecksumAlgorithm>('sha256');

    protected readonly counting = computed(() => Boolean(this.count() && !this.count()!.done));
    protected readonly summing = computed(() => Boolean(this.checksum() && !this.checksum()!.done));
    protected readonly showTags = computed(() => this.config.can(Rights.viewTags));

    protected readonly kind = computed(() => {
        const p = this.properties();
        if (!p)
            return '';
        if (p.details.isFolder)
            return 'Folder';
        const extension = p.item.extension ? `${p.item.extension.replace(/^\./, '').toUpperCase()} ` : '';
        const kinds: Record<string, string> = { image: 'image', video: 'video', audio: 'audio', document: 'document' };
        return `${extension}${kinds[p.item.kind] ?? 'file'}`;
    });
    protected readonly folder = computed(() => {
        const path = this.properties()?.details.path ?? '';
        const root = this.config.config()?.rootName ?? 'the library';
        return !path ? '(the library itself)' : parentPath(path) ? `${root} / ${parentPath(path).split('/').join(' / ')}` : root;
    });
    protected readonly attributes = computed(() => {
        const details = this.properties()?.details;
        return details ? joinParts([details.hidden && 'hidden', details.readOnly && 'read-only', details.isLink && 'a link to another place'], ', ') : '';
    });
    protected readonly media = computed(() => {
        const item = this.properties()?.item;
        return item ? joinParts([formatResolution(item.width, item.height), item.duration ? formatDuration(item.duration) : null]) : '';
    });
    protected readonly totalsText = computed(() => {
        const totals = this.totals();
        if (!totals)
            return 'In the index: …';
        if (!totals.indexed && !totals.files && !totals.folders)
            return 'Not in the index yet: count it on the disk.';
        return `In the index: ${this.holds(totals.files, totals.folders, totals.bytes)}, as of the last scan.`;
    });
    protected readonly countText = computed(() => {
        const job = this.count();
        if (!job)
            return '';
        if (job.error && job.error !== 'Stopped.')
            return job.error;
        const holds = this.holds(job.files, job.folders, job.bytes);
        if (!job.done)
            return `On the disk: ${holds} so far…`;
        return joinParts([
            `On the disk: ${job.capped ? 'more than ' : ''}${holds}${job.error ? ' (stopped)' : ''}.`,
            job.leftOut ? `${plural(job.leftOut, 'link or unreadable folder', 'links or unreadable folders')} left out.` : null,
        ], ' ');
    });
    protected readonly checksumText = computed(() => {
        const job = this.checksum();
        if (!job)
            return '';
        if (job.error)
            return job.error;
        const percent = job.total ? Math.floor(job.bytes * 100 / job.total) : 0;
        return `Reading the file… ${percent}% (${formatSize(job.bytes)} of ${formatSize(job.total)})`;
    });

    private timer: ReturnType<typeof setTimeout> | undefined;
    /** Answers of an earlier item are dropped. */
    private generation = 0;
    private totalsRequest: AbortController | undefined;

    constructor() {
        registerDialog(dialogs => dialogs.properties, this);
        inject(DestroyRef).onDestroy(() => this.closed());
    }

    async open(item: LibraryItem): Promise<void> {
        this.closed();
        const generation = ++this.generation;
        this.item.set(item);
        this.properties.set(null);
        this.totals.set(null);
        this.error.set(null);
        this.count.set(null);
        this.checksum.set(null);
        this.sheet().open();
        try {
            const properties = await this.api.properties(item.path);
            if (generation !== this.generation)
                return;
            this.properties.set(properties);
            if (properties.details.isFolder)
                void this.loadTotals(item.path, generation);
        } catch (error) {
            if (generation === this.generation)
                this.error.set(`The properties could not be read: ${(error as Error).message}`);
        }
    }

    protected size(bytes: number | null): string {
        return bytes === null ? '' : bytes < 1024 ? plural(bytes, 'byte') : `${formatSize(bytes)} (${bytes.toLocaleString()} bytes)`;
    }

    protected date(value: string): string {
        return formatDate(value);
    }

    protected async startCount(): Promise<void> {
        await this.start(() => this.api.countFolder(this.item()!.path), this.count);
    }

    protected async startChecksum(): Promise<void> {
        await this.start(() => this.api.startChecksum(this.item()!.path, this.algorithm()), this.checksum);
    }

    protected stop(job: PropertyJob | null): void {
        if (job && !job.done)
            void this.api.cancelPropertyJob(job.id).catch(() => { });
    }

    protected async copy(text: string): Promise<void> {
        try {
            await copyText(text);
            this.toasts.show('Checksum copied.');
        } catch {
            this.toasts.error('The browser refused to copy.');
        }
    }

    /** The sheet closed (or another item opened): its jobs stop, its requests are dropped. */
    protected closed(): void {
        this.generation++;
        clearTimeout(this.timer);
        this.totalsRequest?.abort();
        this.stop(this.count());
        this.stop(this.checksum());
    }

    private holds(files: number, folders: number, bytes: number): string {
        return `${joinParts([plural(files, 'file'), plural(folders, 'folder')], ', ')}, ${formatSize(bytes)}`;
    }

    private async loadTotals(path: string, generation: number): Promise<void> {
        this.totalsRequest = new AbortController();
        try {
            const totals = await this.api.folderTotals(path, this.totalsRequest.signal);
            if (generation === this.generation)
                this.totals.set(totals);
        } catch {
            if (generation === this.generation)
                this.totals.set({ indexed: false, files: 0, folders: 0, bytes: 0 });
        }
    }

    private async start(begin: () => Promise<PropertyJob>, target: WritableSignal<PropertyJob | null>): Promise<void> {
        const generation = this.generation;
        try {
            const job = await begin();
            if (generation !== this.generation) {
                this.stop(job);
                return;
            }
            target.set(job);
            this.follow(generation);
        } catch (error) {
            this.toasts.error((error as Error).message);
        }
    }

    /** Asks for the running jobs until they end. */
    private follow(generation: number): void {
        clearTimeout(this.timer);
        this.timer = setTimeout(async () => {
            for (const target of [this.count, this.checksum]) {
                const job = target();
                if (!job || job.done)
                    continue;
                try {
                    const now = await this.api.propertyJob(job.id);
                    if (generation === this.generation)
                        target.set(now);
                } catch {
                    if (generation === this.generation)
                        target.set({ ...job, done: true, error: 'The server forgot it: start it again.' });
                }
            }
            if (generation === this.generation && (this.counting() || this.summing()))
                this.follow(generation);
        }, POLL);
    }
}
