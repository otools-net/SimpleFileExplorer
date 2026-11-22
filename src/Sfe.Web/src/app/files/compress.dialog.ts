import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { LibraryItem } from '../core/models';
import { baseName } from '../core/paths';
import { Rights } from '../core/rights';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf, valueOf } from '../shared/events';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { FileTransfers } from './file-transfers';
import { folderBrowser, FolderBrowserComponent } from './folder-browser.component';
import { nameOrCount, sizeToRead } from './file-dialog-texts';

/** Folders whose size is asked from the index at most; the others are said not counted. */
const MEASURED_FOLDERS = 50;

/** The levels offered (the server takes 0 to 9). */
export const COMPRESSION_LEVELS: readonly { level: number; text: string }[] = [
    { level: 0, text: $localize`Store: no compression, the fastest and the largest` },
    { level: 1, text: $localize`Fastest: quick, a little larger` },
    { level: 6, text: $localize`Normal: the usual balance` },
    { level: 9, text: $localize`Maximum: the smallest, several times slower` },
];

/**
 * Compress…: the zip's name, how hard to compress (Store, Fastest, Normal, Maximum; files already compressed stored),
 * a password (AES-256: the names stay readable); the zip is made in the folder listed, in the background
 * (agents/docs/features/compression.md).
 */
@Component({
    selector: 'sfe-compress-dialog',
    imports: [BusyDirective, FolderBrowserComponent, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './compress.dialog.html',
})
export class CompressDialog {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly transfers = inject(FileTransfers);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly levels = COMPRESSION_LEVELS;
    private readonly items = signal<LibraryItem[]>([]);
    /** Where the zip goes: the folder listed, or one picked (Another folder…). */
    protected readonly browser = folderBrowser();
    protected readonly target = this.browser.target;
    protected readonly picking = signal(false);
    /** What will be read, from the index ("12.4 MB to read"); null while counting. */
    protected readonly size = signal<string | null>(null);
    private sizeRequest: AbortController | null = null;
    protected readonly name = signal('');
    protected readonly level = signal(6);
    protected readonly skipCompressed = signal(true);
    protected readonly password = signal('');
    protected readonly confirmation = signal('');
    protected readonly showPassword = signal(false);
    protected readonly busy = signal(false);
    /** The page came over plain HTTP from another computer: what is typed crosses the network as is. */
    protected readonly plainHttp =
        location.protocol === 'http:' && !['localhost', '127.0.0.1', '[::1]'].includes(location.hostname);

    protected readonly targetName = computed(() => baseName(this.target()) || this.config.libraryName());
    protected readonly what = computed(() => {
        const what = nameOrCount(this.items().map((item) => item.name));
        return $localize`Put ${what}:items: into a zip in “${this.targetName()}:folder:”.`;
    });
    protected readonly ascii = computed(() => [...this.password()].every((c) => c >= ' ' && c <= '~'));
    /** Why it can't start yet; null when it can. */
    protected readonly problem = computed(() => {
        const name = this.name().trim();
        if (!name) return $localize`Give the zip a name.`;
        if (/[\\/:*?"<>|]/.test(name)) return $localize`A name can't hold \\ / : * ? " < > |.`;
        if (this.password() && this.password() !== this.confirmation()) return $localize`The two passwords differ.`;
        if (!this.browser.listing())
            return this.browser.loading() ? $localize`Reading the folder…` : $localize`Pick a folder.`;
        if (!this.browser.allows(Rights.compressFiles))
            return $localize`You may not make a zip in this folder. Pick another.`;
        return null;
    });

    constructor() {
        registerDialog((dialogs) => dialogs.compress, this);
    }

    /** Compress… of `items`: the zip goes into the folder listed (in search results, the first item's). */
    open(items: LibraryItem[]): void {
        if (!items.length) return;
        this.items.set(items);
        const target = this.transfers.compressTarget(items);
        this.browser.reset(target);
        void this.browser.browse(target);
        this.picking.set(false);
        this.name.set(defaultName(items, target, this.config.libraryName()));
        void this.measure(items);
        this.level.set(6);
        this.skipCompressed.set(true);
        this.password.set('');
        this.confirmation.set('');
        this.showPassword.set(false);
        this.sheet().open();
    }

    /** The size to read: the files' own, the folders' from the index (those never scanned are said, not guessed). */
    private async measure(items: LibraryItem[]): Promise<void> {
        this.sizeRequest?.abort();
        const request = (this.sizeRequest = new AbortController());
        this.size.set(null);
        let bytes = items.filter((item) => item.kind !== 'directory').reduce((sum, item) => sum + (item.size ?? 0), 0);
        let files = items.length - items.filter((item) => item.kind === 'directory').length;
        let unknown = 0;
        const folders = items.filter((item) => item.kind === 'directory');
        for (const folder of folders.slice(0, MEASURED_FOLDERS)) {
            try {
                const totals = await this.api.folderTotals(folder.path, request.signal);
                if (request.signal.aborted) return;
                if (totals?.indexed) {
                    bytes += totals.bytes;
                    files += totals.files;
                } else {
                    unknown++;
                }
            } catch {
                if (request.signal.aborted) return;
                unknown++;
            }
        }
        unknown += Math.max(0, folders.length - MEASURED_FOLDERS);
        this.size.set(sizeToRead(bytes, files, unknown));
    }

    protected checked(event: Event): boolean {
        return checkedOf(event);
    }

    protected value(event: Event): string {
        return valueOf(event);
    }

    protected async confirm(): Promise<void> {
        if (this.problem()) return;
        const started = await whileBusy(this.busy, () =>
            this.transfers.compress({
                paths: this.items().map((item) => item.path),
                target: this.target(),
                name: this.name().trim(),
                level: this.level(),
                skipCompressed: this.skipCompressed(),
                password: this.password() || null,
            }),
        );
        // The password isn't kept: the job has it while it runs.
        this.password.set('');
        this.confirmation.set('');
        if (started) {
            this.store.clearSelection();
            this.sheet().close();
        }
    }
}

/** The zip's name before it is changed: its only item's (a file without its extension), else the folder's. */
export function defaultName(items: LibraryItem[], target: string, rootName: string): string {
    if (items.length === 1) {
        const name = items[0].name;
        const dot = name.lastIndexOf('.');
        return items[0].kind === 'directory' || dot <= 0 ? name : name.slice(0, dot);
    }
    return baseName(target) || rootName;
}
