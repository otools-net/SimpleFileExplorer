import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { joinParts, plural } from '../core/format';
import { LibraryItem } from '../core/models';
import { AccessGain, FileJobKind, FileJobRequest } from './file.models';
import { baseName, parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf } from '../shared/events';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { FileTransfers } from './file-transfers';
import { PlaceTransfers } from './place-transfers';
import { UsableConnections } from '../core/usable-connections';
import { folderBrowser, FolderBrowserComponent } from './folder-browser.component';

/**
 * Move to… and Copy to…: picks the folder by clicking through the library or a connection the session writes to, then
 * starts the job (between two places, a transfer job: `FileTransfers.transfer`). Also the confirmation
 * of a paste or a drop: a copy asks whether the tags come along, a move into a place where more people would see or
 * change a folder says who, and offers to keep the access the folder has now.
 */
@Component({
    selector: 'sfe-transfer-dialog',
    imports: [BusyDirective, FolderBrowserComponent, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './transfer.dialog.html',
})
export class TransferDialog {
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly transfers = inject(FileTransfers);
    private readonly placeTransfers = inject(PlaceTransfers);
    private readonly connections = inject(UsableConnections);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly kind = signal<FileJobKind>('move');
    protected readonly step = signal<'pick' | 'confirm'>('pick');
    private readonly paths = signal<string[]>([]);
    private readonly items = signal<LibraryItem[]>([]);
    /** Where the items are: a connection's id, null for the library. */
    private readonly from = signal<number | null>(null);
    /** The folder chosen (picker), or where the paste or drop goes. */
    protected readonly browser = folderBrowser();
    private readonly target = this.browser.target;
    protected readonly busy = signal(false);
    protected readonly gains = signal<AccessGain[]>([]);
    protected readonly keepAccess = signal(false);
    protected readonly keepTags = signal(true);
    /** The folder chosen is in the place the items are. */
    private readonly samePlace = computed(() => this.browser.connection() === this.from());
    /** Tags are the library's: asked about for a copy within it only. */
    protected readonly tagsAsked = computed(
        () => this.kind() === 'copy' && this.from() === null && this.browser.connection() === null,
    );
    /** Between two places: the files' tags go with them (the library keeps the tags of connection files too). */
    protected readonly tagsTravel = computed(() => this.from() !== this.browser.connection());

    protected readonly what = computed(() => {
        const items = this.items();
        const folders = items.filter((item) => item.kind === 'directory').length;
        const files = items.length - folders;
        const counted = joinParts(
            [folders ? plural(folders, 'folder') : null, files ? plural(files, 'file') : null],
            ' and ',
        );
        const what = items.length === 1 ? `“${items[0].name}”` : counted || plural(this.paths().length, 'item');
        return `${this.kind() === 'move' ? 'Move' : 'Copy'} ${what}`;
    });
    protected readonly question = computed(() => {
        const count = this.paths().length;
        const what = count === 1 ? `“${baseName(this.paths()[0])}”` : plural(count, 'item');
        return `${this.kind() === 'move' ? 'Move' : 'Copy'} ${what} into “${this.targetName()}”?`;
    });
    protected readonly gainedFolders = computed(() => {
        const folders = new Set(this.gains().map((gain) => gain.folder));
        return folders.size === 1 ? `“${baseName([...folders][0])}”` : plural(folders.size, 'folder');
    });
    /** Why the folder chosen can't be used; null when it can. */
    protected readonly refusal = computed(() => {
        const listing = this.browser.listing();
        if (!listing) return 'Pick a folder.';
        if (!this.browser.allows(this.kind() === 'move' ? Rights.moveFiles : Rights.copyFiles))
            return `You may not ${this.kind()} files into this folder: open another.`;
        if (
            this.kind() === 'move' &&
            this.samePlace() &&
            this.paths().every((path) => parentPath(path) === listing.path)
        )
            return 'They are in this folder already: open another.';
        return null;
    });
    protected readonly confirmLabel = computed(() =>
        this.step() === 'pick'
            ? this.kind() === 'move'
                ? 'Move here'
                : 'Copy here'
            : this.kind() === 'move'
              ? 'Move'
              : 'Copy',
    );
    protected readonly confirmTitle = computed(
        () => `${this.kind() === 'move' ? 'Move' : 'Copy'} into “${this.targetName()}”: it goes on in the background`,
    );
    private readonly targetName = computed(() => baseName(this.target()) || this.browser.placeName());

    constructor() {
        registerDialog((dialogs) => dialogs.transfer, this);
    }

    /** Move to… / Copy to…: the folder is picked, starting from the one listed. */
    pick(kind: FileJobKind, items: LibraryItem[]): void {
        this.reset(
            kind,
            items.map((item) => item.path),
            'pick',
        );
        this.items.set(items);
        this.from.set(this.config.place()?.id ?? null);
        const route = this.store.route();
        void this.browser.browse(route.q ? parentPath(items[0]?.path ?? '') : route.path, this.from());
        void this.connections.load(true).then((usable) => {
            if (usable.length) this.browser.offer(usable);
        });
        this.sheet().open();
    }

    /**
     * A paste or a drop into a known folder: the tags of a copy, the access gained by a move; a move within
     * `connection` (`PlaceTransfers`) is answered there.
     */
    ask(request: FileJobRequest, gains: AccessGain[], connection: number | null = null): void {
        this.reset(request.kind, request.paths, 'confirm');
        this.from.set(connection);
        this.browser.reset(request.target, connection);
        this.gains.set(gains);
        this.sheet().open();
    }

    private reset(kind: FileJobKind, paths: string[], step: 'pick' | 'confirm'): void {
        this.kind.set(kind);
        this.paths.set(paths);
        this.items.set([]);
        this.from.set(null);
        this.browser.places.set(null);
        this.step.set(step);
        this.gains.set([]);
        this.keepAccess.set(false);
        this.keepTags.set(true);
        this.browser.reset();
    }

    /** A folder moved can't take itself. */
    protected readonly closedBecause = (path: string): string | null =>
        this.kind() === 'move' &&
        this.samePlace() &&
        this.paths().some((moved) => path === moved || path.startsWith(`${moved}/`))
            ? 'It is moved itself'
            : null;

    protected gainText(gain: AccessGain): string {
        const now = gain.before === 'hidden' ? "who don't see it now" : 'who may only look now';
        const then = gain.after === 'write' ? 'could change' : 'would see';
        return `${gain.isGroup ? `The group ${gain.who}` : gain.who} (${now}) ${then} “${baseName(gain.folder)}”.`;
    }

    protected checked(event: Event): boolean {
        return checkedOf(event);
    }

    protected async confirm(): Promise<void> {
        const request: FileJobRequest = { kind: this.kind(), paths: this.paths(), target: this.target() };
        if (this.step() === 'confirm' && this.from() !== null && this.gains().length) {
            await whileBusy(this.busy, () => this.placeTransfers.answer(this.keepAccess()));
            this.sheet().close();
            return;
        }
        if (this.from() !== null || this.browser.connection() !== null) {
            await this.elsewhere(request);
            return;
        }
        if (this.step() === 'pick' && request.kind === 'move') {
            const gains = await whileBusy(this.busy, () => this.transfers.gains(request.paths, request.target));
            if (gains === null) return;
            if (gains.length) {
                this.gains.set(gains);
                this.step.set('confirm');
                return;
            }
        }
        if (request.kind === 'move') request.keepAccess = this.keepAccess();
        else request.keepTags = this.keepTags();
        const started = await whileBusy(this.busy, () => this.transfers.start(request));
        if (started) {
            this.store.clearSelection();
            this.sheet().close();
        }
    }

    /** Into a connection, or out of one: its provider's move or copy, or a transfer job between the two places. */
    private async elsewhere({ kind, paths, target }: FileJobRequest): Promise<void> {
        const places = { from: this.from(), to: this.browser.connection() };
        await whileBusy(this.busy, () => this.transfers.transfer(kind, paths, target, places));
        this.store.clearSelection();
        this.sheet().close();
    }
}
