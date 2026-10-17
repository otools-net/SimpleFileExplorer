import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { joinParts, plural } from '../core/format';
import { AccessGain, DirectoryListing, FileJobKind, FileJobRequest, LibraryItem } from '../core/models';
import { baseName, parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { SheetComponent } from '../shared/sheet.component';
import { registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { FileTransfers } from './file-transfers';

/** Folders listed at most in a folder of the picker (open one to go further). */
const FOLDERS_SHOWN = 200;

/**
 * Move to… and Copy to…: picks the folder by clicking through the library, then starts the job. Also the confirmation
 * of a paste or a drop: a copy asks whether the tags come along, a move into a place where more people would see or
 * change a folder says who, and offers to keep the access the folder has now.
 */
@Component({
    selector: 'sfe-transfer-dialog',
    imports: [BusyDirective, IconComponent, SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet [heading]="kind() === 'move' ? 'Move' : 'Copy'" variant="transfer-dialog">
            @if (step() === 'pick') {
                <p>{{ what() }} into:</p>
                <nav class="transfer-crumbs" aria-label="The folder chosen">
                    @for (crumb of crumbs(); track crumb.path; let last = $last) {
                        @if (last) {
                            <strong><svg sfeIcon="folder"></svg>{{ crumb.name }}</strong>
                        } @else {
                            <button type="button" class="link-button" [title]="'Go back to ' + crumb.name" (click)="browse(crumb.path)">{{ crumb.name }}</button>
                            <span aria-hidden="true">/</span>
                        }
                    }
                </nav>
                <ul class="transfer-folders">
                    @for (folder of folders(); track folder.path) {
                        <li>
                            <button type="button" class="transfer-folder" [disabled]="isMoved(folder.path)"
                                    [title]="isMoved(folder.path) ? 'It is moved itself' : 'Open ' + folder.name" (click)="browse(folder.path)">
                                <svg sfeIcon="folder"></svg><span>{{ folder.name }}</span>
                            </button>
                        </li>
                    } @empty {
                        <li class="muted">{{ loading() ? 'Loading…' : 'No folders in here.' }}</li>
                    }
                    @if (truncated()) {
                        <li class="muted">Only the first {{ shown }} folders are listed.</li>
                    }
                </ul>
                @if (refusal(); as refusal) {
                    <p class="field-hint">{{ refusal }}</p>
                }
            } @else {
                <p>{{ question() }}</p>
                @if (gains().length) {
                    <p>There, more people would see or change {{ gainedFolders() }}:</p>
                    <ul class="transfer-gains">
                        @for (gain of gains(); track $index) {
                            <li>{{ gainText(gain) }}</li>
                        }
                    </ul>
                    <label class="check">
                        <input type="checkbox" [checked]="keepAccess()" (change)="keepAccess.set(checked($event))">
                        <span>Keep the access they have now<span class="field-hint">Folder rules keep who sees and changes them as before the move.</span></span>
                    </label>
                }
            }
            @if (kind() === 'copy') {
                <label class="check">
                    <input type="checkbox" [checked]="keepTags()" (change)="keepTags.set(checked($event))">
                    <span>Copy their tags too<span class="field-hint">The copies get the tags of the files they come from.</span></span>
                </label>
            }
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close without moving or copying anything (Esc)" (click)="sheet.close()">Cancel</button>
            <button sheetFooter type="button" class="button primary" [title]="confirmTitle()" [sfeBusy]="busy()"
                    [disabled]="busy() || (step() === 'pick' && refusal() !== null)" (click)="confirm()">{{ confirmLabel() }}</button>
        </sfe-sheet>
    `,
})
export class TransferDialog {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly transfers = inject(FileTransfers);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');

    protected readonly shown = FOLDERS_SHOWN;
    protected readonly kind = signal<FileJobKind>('move');
    protected readonly step = signal<'pick' | 'confirm'>('pick');
    private readonly paths = signal<string[]>([]);
    private readonly items = signal<LibraryItem[]>([]);
    /** The folder chosen (picker), or where the paste or drop goes. */
    protected readonly target = signal('');
    private readonly listing = signal<DirectoryListing | null>(null);
    protected readonly loading = signal(false);
    protected readonly busy = signal(false);
    protected readonly gains = signal<AccessGain[]>([]);
    protected readonly keepAccess = signal(false);
    protected readonly keepTags = signal(true);

    protected readonly folders = computed(() => this.listing()?.items.filter(item => item.kind === 'directory') ?? []);
    protected readonly truncated = computed(() => (this.listing()?.directoryCount ?? 0) > this.folders().length);
    protected readonly crumbs = computed(() => {
        const root = { path: '', name: this.config.config()?.rootName || 'Library' };
        const parts = this.target() ? this.target().split('/') : [];
        return [root, ...parts.map((name, index) => ({ path: parts.slice(0, index + 1).join('/'), name }))];
    });
    protected readonly what = computed(() => {
        const items = this.items();
        const folders = items.filter(item => item.kind === 'directory').length;
        const files = items.length - folders;
        const counted = joinParts([folders ? plural(folders, 'folder') : null, files ? plural(files, 'file') : null], ' and ');
        const what = items.length === 1 ? `“${items[0].name}”` : counted || plural(this.paths().length, 'item');
        return `${this.kind() === 'move' ? 'Move' : 'Copy'} ${what}`;
    });
    protected readonly question = computed(() => {
        const count = this.paths().length;
        const what = count === 1 ? `“${baseName(this.paths()[0])}”` : plural(count, 'item');
        return `${this.kind() === 'move' ? 'Move' : 'Copy'} ${what} into “${this.targetName()}”?`;
    });
    protected readonly gainedFolders = computed(() => {
        const folders = new Set(this.gains().map(gain => gain.folder));
        return folders.size === 1 ? `“${baseName([...folders][0])}”` : plural(folders.size, 'folder');
    });
    /** Why the folder chosen can't be used; null when it can. */
    protected readonly refusal = computed(() => {
        const listing = this.listing();
        if (!listing)
            return 'Pick a folder.';
        const right = this.kind() === 'move' ? Rights.moveFiles : Rights.copyFiles;
        const allowed = listing.access ? !listing.access.pathOnly && listing.access.rights.includes(right) : this.config.can(right);
        if (!allowed)
            return `You may not ${this.kind()} files into this folder: open another.`;
        if (this.kind() === 'move' && this.paths().every(path => parentPath(path) === listing.path))
            return 'They are in this folder already: open another.';
        return null;
    });
    protected readonly confirmLabel = computed(() =>
        this.step() === 'pick' ? (this.kind() === 'move' ? 'Move here' : 'Copy here') : (this.kind() === 'move' ? 'Move' : 'Copy'));
    protected readonly confirmTitle = computed(() =>
        `${this.kind() === 'move' ? 'Move' : 'Copy'} into “${this.targetName()}”: it goes on in the background`);
    private readonly targetName = computed(() => baseName(this.target()) || this.config.config()?.rootName || 'the library');

    constructor() {
        registerDialog(dialogs => dialogs.transfer, this);
    }

    /** Move to… / Copy to…: the folder is picked, starting from the one listed. */
    pick(kind: FileJobKind, items: LibraryItem[]): void {
        this.reset(kind, items.map(item => item.path), 'pick');
        this.items.set(items);
        const route = this.store.route();
        void this.browse(route.q ? parentPath(items[0]?.path ?? '') : route.path);
        this.sheet().open();
    }

    /** A paste or a drop into a known folder: the tags of a copy, the access gained by a move. */
    ask(request: FileJobRequest, gains: AccessGain[]): void {
        this.reset(request.kind, request.paths, 'confirm');
        this.target.set(request.target);
        this.gains.set(gains);
        this.sheet().open();
    }

    private reset(kind: FileJobKind, paths: string[], step: 'pick' | 'confirm'): void {
        this.kind.set(kind);
        this.paths.set(paths);
        this.items.set([]);
        this.step.set(step);
        this.gains.set([]);
        this.keepAccess.set(false);
        this.keepTags.set(true);
        this.listing.set(null);
    }

    protected async browse(path: string): Promise<void> {
        this.target.set(path);
        this.loading.set(true);
        try {
            const listing = await this.api.browse({ path, offset: 0, limit: FOLDERS_SHOWN, sort: 'name', desc: false });
            if (this.target() === path)
                this.listing.set(listing);
        } catch {
            // A folder gone meanwhile: back to the top.
            if (path)
                await this.browse('');
        } finally {
            this.loading.set(false);
        }
    }

    /** A folder moved can't take itself. */
    protected isMoved(path: string): boolean {
        return this.kind() === 'move' && this.paths().some(moved => path === moved || path.startsWith(`${moved}/`));
    }

    protected gainText(gain: AccessGain): string {
        const now = gain.before === 'hidden' ? 'who don\'t see it now' : 'who may only look now';
        const then = gain.after === 'write' ? 'could change' : 'would see';
        return `${gain.isGroup ? `The group ${gain.who}` : gain.who} (${now}) ${then} “${baseName(gain.folder)}”.`;
    }

    protected checked(event: Event): boolean {
        return checkedOf(event);
    }

    protected async confirm(): Promise<void> {
        const request: FileJobRequest = { kind: this.kind(), paths: this.paths(), target: this.target() };
        if (this.step() === 'pick' && request.kind === 'move') {
            const gains = await whileBusy(this.busy, () => this.transfers.gains(request.paths, request.target));
            if (gains === null)
                return;
            if (gains.length) {
                this.gains.set(gains);
                this.step.set('confirm');
                return;
            }
        }
        if (request.kind === 'move')
            request.keepAccess = this.keepAccess();
        else
            request.keepTags = this.keepTags();
        const started = await whileBusy(this.busy, () => this.transfers.start(request));
        if (started) {
            this.store.clearSelection();
            this.sheet().close();
        }
    }
}
