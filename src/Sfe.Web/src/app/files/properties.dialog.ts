import {
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    inject,
    signal,
    viewChild,
    WritableSignal,
} from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { copyText, CopyPathService } from '../core/copy-path.service';
import { formatDate, formatDuration, formatResolution, joinParts } from '../core/format';
import { PlaceProperties } from '../core/connection.models';
import { ItemProperties, LibraryItem } from '../core/models';
import { ChecksumAlgorithm, FolderTotals, PropertyJob } from './file.models';
import { placeProperties, providerName } from './place-properties';
import {
    attributesText,
    checksumText,
    countText,
    folderOf,
    itemKind,
    jobForgotten,
    sizeText,
    totalsText,
} from './properties-texts';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { registerDialog } from '../library/dialogs.service';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from '../tags/tag-chip.component';

/** How often (ms) a running count or checksum is asked for. */
const POLL = 400;

const ALGORITHMS: [ChecksumAlgorithm, string][] = [
    ['sha256', 'SHA-256'],
    ['sha1', 'SHA-1'],
    ['md5', 'MD5'],
];

/**
 * Properties of a file or folder (Alt+Enter, or its menu): what the disk says (size, dates, attributes), what the index
 * knows (resolution, duration, tags; a folder's totals), and on demand a folder counted on the disk or a file's
 * checksum, which run on the server and stop when the sheet closes. On a connection (`?c=`), what its provider tells.
 */
@Component({
    selector: 'sfe-properties-dialog',
    imports: [SheetComponent, TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './properties.dialog.html',
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
    /** The answer on a connection (`config.place()`), instead of `properties`. */
    protected readonly remote = signal<PlaceProperties | null>(null);
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
        return !p ? '' : p.details.isFolder ? $localize`Folder` : itemKind(p.item);
    });
    protected readonly folder = computed(() => {
        const root = this.config.config()?.rootName ?? $localize`The library`;
        return folderOf(this.properties()?.details.path ?? '', root, false);
    });
    protected readonly remoteKind = computed(() => {
        const item = this.remote()?.item;
        return item ? itemKind(item) : '';
    });
    protected readonly remoteFolder = computed(() => {
        const name = this.config.place()?.name || $localize`The connection`;
        return folderOf(this.remote()?.item.path ?? '', name, true);
    });
    protected readonly provider = computed(() => providerName(this.config.config(), this.remote()?.kind ?? ''));
    protected readonly onPlace = computed(() => this.config.place() !== null);
    protected readonly attributes = computed(() => {
        const details = this.properties()?.details;
        return details ? attributesText(details) : '';
    });
    protected readonly media = computed(() => {
        const item = this.properties()?.item;
        return item
            ? joinParts([
                  formatResolution(item.width, item.height),
                  item.duration ? formatDuration(item.duration) : null,
              ])
            : '';
    });
    protected readonly totalsText = computed(() => totalsText(this.totals()));
    protected readonly countText = computed(() => countText(this.count()));
    protected readonly checksumText = computed(() => checksumText(this.checksum()));
    /** The name of a connection's content hash ("Dropbox content hash"). */
    protected readonly hashName = computed(() => this.remote()?.hashName ?? $localize`Content hash`);

    private timer: ReturnType<typeof setTimeout> | undefined;
    /** Answers of an earlier item are dropped. */
    private generation = 0;
    private totalsRequest: AbortController | undefined;

    constructor() {
        registerDialog((dialogs) => dialogs.properties, this);
        inject(DestroyRef).onDestroy(() => this.closed());
    }

    async open(item: LibraryItem): Promise<void> {
        this.closed();
        const generation = ++this.generation;
        this.item.set(item);
        this.properties.set(null);
        this.remote.set(null);
        this.totals.set(null);
        this.error.set(null);
        this.count.set(null);
        this.checksum.set(null);
        this.sheet().open();
        try {
            const place = this.config.place();
            if (place) {
                const remote = await placeProperties(this.api, place, item.path);
                if (generation === this.generation) this.remote.set(remote);
                return;
            }
            const properties = await this.api.properties(item.path);
            if (generation !== this.generation) return;
            this.properties.set(properties);
            if (properties.details.isFolder) void this.loadTotals(item.path, generation);
        } catch (error) {
            if (generation === this.generation) this.error.set((error as Error).message);
        }
    }

    protected size(bytes: number | null): string {
        return sizeText(bytes);
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
        if (job && !job.done) void this.api.cancelPropertyJob(job.id).catch(() => {});
    }

    protected async copy(text: string, what = 'Checksum'): Promise<void> {
        try {
            await copyText(text);
            this.toasts.show(`${what} copied.`);
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

    private async loadTotals(path: string, generation: number): Promise<void> {
        this.totalsRequest = new AbortController();
        try {
            const totals = await this.api.folderTotals(path, this.totalsRequest.signal);
            if (generation === this.generation) this.totals.set(totals);
        } catch {
            if (generation === this.generation) this.totals.set({ indexed: false, files: 0, folders: 0, bytes: 0 });
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
                if (!job || job.done) continue;
                try {
                    const now = await this.api.propertyJob(job.id);
                    if (generation === this.generation) target.set(now);
                } catch {
                    if (generation === this.generation) target.set({ ...job, done: true, error: jobForgotten() });
                }
            }
            if (generation === this.generation && (this.counting() || this.summing())) this.follow(generation);
        }, POLL);
    }
}
