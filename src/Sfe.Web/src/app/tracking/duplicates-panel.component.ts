import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatSize, joinParts, plural } from '../core/format';
import { DuplicateGroup, DuplicateResults, FileTags, isFileConflict, LibraryItem } from '../core/models';
import { readPreference, writePreference } from '../core/preferences';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf, valueOf } from '../shared/events';
import { StatusBoxComponent, TrackingFileComponent } from './tracking-parts';

const SCOPE_KEY = 'sfe.duplicates.folderOnly';

/** A copy removed from a group: the copy that was kept, with its tags now. */
export interface RemovedCopy {
    file: LibraryItem;
    keepPath: string;
    kept: FileTags;
    /** Fewer than two copies are left: the group is no longer one. */
    dissolved: boolean;
}

/** A removed copy put back from the trash (Undo). */
export interface RestoredCopy {
    file: LibraryItem;
    /** The group had a single copy left: it is one again. */
    revived: boolean;
}

/**
 * A group of identical files. When the server allows it, a copy can be removed: "Remove" opens a confirmation under
 * the file, where the copy getting its tags is chosen. It goes to the trash (Undo in the message that says so), or for
 * good when the space deletes permanently (`permanent`).
 */
@Component({
    selector: 'section[sfeDuplicateGroup]',
    imports: [BusyDirective, TrackingFileComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'tracking-group' },
    template: `
        <header class="tracking-group-head">
            <strong>{{ countText() }}</strong>
            <span>{{ sizeText() }}</span>
        </header>
        <ul class="tracking-list">
            @for (file of files(); track file.path) {
                <li [sfeTrackingFile]="file" (openFolder)="openFolder.emit($event)">
                    @if (canRemove()) {
                        <span class="tracking-actions">
                            <button type="button" class="button small danger" [title]="permanent() ? 'Delete this copy for good, keeping another one (asks first)' : 'Move this copy to the trash, keeping another one (asks first)'" (click)="confirm(file)">Remove</button>
                        </span>
                    }
                    @if (confirming() === file.path) {
                        @let others = othersOf(file);
                        <div class="tracking-confirm" role="group" [attr.aria-label]="'Remove ' + file.name">
                            <span>
                                {{ permanent() ? 'Delete this file for good' : 'Move this file to the trash' }}{{ file.tags?.length ? ' and add its tags to ' : ' and keep ' }}
                                @if (others.length > 1) {
                                    <select class="tracking-keep" aria-label="Copy to keep" (change)="keep.set(valueOf($event))">
                                        @for (other of others; track other.path) {
                                            <option [value]="other.path" [selected]="other.path === keep()">{{ other.path }}</option>
                                        }
                                    </select>
                                } @else {
                                    <strong>{{ others[0].path }}</strong>
                                }?
                            </span>
                            <span class="tracking-actions">
                                <button type="button" class="button small" title="Keep the file" (click)="confirming.set(null)">Cancel</button>
                                <button type="button" class="button small danger" [title]="permanent() ? 'Delete this file from the disk now' : 'Move this file to the trash now (it can be put back)'" [sfeBusy]="removing()" [disabled]="removing()" (click)="remove(file)">{{ permanent() ? 'Delete this copy' : 'Move to the trash' }}</button>
                            </span>
                        </div>
                    }
                </li>
            }
        </ul>
        @if (hidden() > 0) {
            <p class="tracking-more">and {{ hiddenText() }}</p>
        }
    `,
})
export class DuplicateGroupComponent {
    readonly group = input.required<DuplicateGroup>({ alias: 'sfeDuplicateGroup' });
    readonly canRemove = input(false);
    /** Copies removed are deleted for good (`Files:DeletePermanently`), not moved to the trash. */
    readonly permanent = input(false);
    readonly openFolder = output<string>();
    readonly removed = output<RemovedCopy>();
    readonly restored = output<RestoredCopy>();

    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly removedPaths = signal<ReadonlySet<string>>(new Set());
    private readonly keptTags = signal<ReadonlyMap<string, FileTags['tags']>>(new Map());
    protected readonly confirming = signal<string | null>(null);
    protected readonly keep = signal('');
    protected readonly removing = signal(false);
    protected readonly valueOf = valueOf;
    private destroyed = false;

    constructor() {
        // An Undo can come after the panel was reloaded.
        inject(DestroyRef).onDestroy(() => this.destroyed = true);
    }

    protected readonly files = computed(() => this.group().files
        .filter(file => !this.removedPaths().has(file.path))
        .map(file => this.keptTags().has(file.path) ? { ...file, tags: this.keptTags().get(file.path) } : file));
    private readonly count = computed(() => this.group().count - this.removedPaths().size);
    protected readonly hidden = computed(() => this.count() - this.files().length);
    protected readonly countText = computed(() => plural(this.count(), 'copy', 'copies'));
    protected readonly sizeText = computed(() => `${formatSize(this.group().size)} each · ${formatSize(this.group().size * (this.count() - 1))} extra`);
    protected readonly hiddenText = computed(() => plural(this.hidden(), 'more file', 'more files'));

    protected othersOf(file: LibraryItem): LibraryItem[] {
        return this.files().filter(other => other.path !== file.path);
    }

    protected confirm(file: LibraryItem): void {
        this.confirming.set(file.path);
        this.keep.set(this.othersOf(file)[0]?.path ?? '');
    }

    protected async remove(file: LibraryItem): Promise<void> {
        const keepPath = this.keep();
        try {
            const result = await whileBusy(this.removing, () => this.api.removeDuplicate(file.path, keepPath));
            this.confirming.set(null);
            this.removedPaths.update(paths => new Set([...paths, file.path]));
            this.keptTags.update(tags => new Map(tags).set(keepPath, result.kept.tags));
            const kept = `“${keepPath}” is kept${result.kept.tags.length ? ' with its tags' : ''}.`;
            const trashId = result.trashId;
            if (typeof trashId === 'number')
                this.toasts.show(`“${file.name}” moved to the trash; ${kept}`, {
                    timeout: 10000,
                    action: { label: 'Undo', title: 'Put it back where it was', run: () => void this.undo(file, trashId) },
                });
            else
                this.toasts.show(`“${file.name}” deleted; ${kept}`);
            this.removed.emit({ file, keepPath, kept: result.kept, dissolved: this.count() < 2 });
        } catch (error) {
            this.toasts.error(`Could not remove “${file.name}”: ${(error as Error).message}`, 8000);
        }
    }

    /** Puts a removed copy back from the trash; the tags it gave to the copy kept stay there. */
    private async undo(file: LibraryItem, trashId: number): Promise<void> {
        try {
            const result = await this.api.restoreFromTrash(trashId);
            if (isFileConflict(result)) {
                this.toasts.error(result.detail ?? `“${file.name}” could not be put back.`);
                return;
            }
        } catch (error) {
            this.toasts.error(`Could not put “${file.name}” back: ${(error as Error).message}`);
            return;
        }
        this.toasts.show(`“${file.name}” put back.`);
        if (this.destroyed || !this.removedPaths().has(file.path))
            return;
        const revived = this.count() < 2;
        this.removedPaths.update(paths => new Set([...paths].filter(path => path !== file.path)));
        this.restored.emit({ file, revived });
    }
}

/**
 * Groups of identical files, biggest first, of the whole library or of the folder being browsed (`folder`).
 * `changed` is emitted when a copy was removed (tags moved between files).
 */
@Component({
    selector: 'sfe-duplicates-panel',
    imports: [BusyDirective, DuplicateGroupComponent, StatusBoxComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-panel', role: 'tabpanel' },
    template: `
        <p class="settings-intro">Files of the index with the same size and the same fingerprint, the biggest first. Only indexed files are compared: scan a folder to include its latest files.</p>
        @if (folder()) {
            <label class="check tracking-scope">
                <input type="checkbox" [checked]="folderOnly()" (change)="setFolderOnly(checkedOf($event))">
                <span>Only in “{{ folder() }}”</span>
            </label>
        }
        <sfe-status-box [state]="status().state" [text]="status().text" [detail]="status().detail" />
        <div class="tracking-groups">
            @if (loading()) {
                <div class="tag-loading"><span class="spinner"></span></div>
            }
            @for (group of groups(); track group.fingerprint + group.size) {
                <!-- Hidden rather than dropped: an Undo brings it back. -->
                <section [sfeDuplicateGroup]="group" [canRemove]="canRemove()" [permanent]="permanent()" [hidden]="dissolved().has(group.fingerprint + group.size)"
                         (openFolder)="openFolder.emit($event)" (removed)="onRemoved(group, $event)" (restored)="onRestored(group, $event)"></section>
            }
        </div>
        <button type="button" class="button" title="Show the next page" [hidden]="!hasMore()" [sfeBusy]="loadingMore()" [disabled]="loadingMore()" (click)="more()">Load more</button>
    `,
})
export class DuplicatesPanelComponent {
    readonly folder = input('');
    readonly openFolder = output<string>();
    readonly changed = output<void>();

    private readonly api = inject(Api);
    protected readonly checkedOf = checkedOf;
    protected readonly folderOnly = signal(readPreference(SCOPE_KEY) === '1');
    protected readonly groups = signal<DuplicateGroup[]>([]);
    protected readonly dissolved = signal<ReadonlySet<string>>(new Set());
    protected readonly canRemove = signal(false);
    protected readonly permanent = signal(false);
    protected readonly hasMore = signal(false);
    protected readonly loading = signal(false);
    protected readonly loadingMore = signal(false);
    /** The first page's result: its summary is kept up to date as copies are removed. */
    private readonly first = signal<DuplicateResults | null>(null);
    private readonly failure = signal<string | null>(null);
    private next: string | null = null;
    private generation = 0;

    protected readonly status = computed(() => {
        const failure = this.failure();
        if (failure)
            return { state: 'unavailable', text: `Could not look for duplicates: ${failure}`, detail: '' };
        const result = this.first();
        const summary = result?.summary;
        if (!result || !summary)
            return { state: null, text: '…', detail: '' };
        const where = result.path ? `in “${result.path}”` : 'in the library';
        return {
            state: summary.groups ? 'found' : 'idle',
            text: summary.groups
                ? `${plural(summary.groups, 'group')} of identical files ${where}: ${formatSize(summary.wastedBytes)} could be freed`
                : `No duplicates ${where}.`,
            detail: joinParts([
                summary.groups ? `${plural(summary.files, 'file')} in all` : null,
                result.minimumSize > 0 ? `files under ${formatSize(result.minimumSize)} are left out` : null,
                summary.unfingerprinted ? `${plural(summary.unfingerprinted, 'indexed file')} without a fingerprint can't be compared${result.fingerprinting ? ' until they are scanned again' : ''}` : null,
                !result.fingerprinting ? 'scans don\'t fingerprint files (Scanning › Indexing › Fingerprint files): new files are not compared' : null,
            ]),
        };
    });
    /** For the tab. */
    readonly badge = computed(() => this.first()?.summary?.groups ? this.first()!.summary!.groups.toLocaleString() : '');

    async load({ append = false } = {}): Promise<void> {
        const current = append ? this.generation : ++this.generation;
        if (!append) {
            this.groups.set([]);
            this.dissolved.set(new Set());
            this.hasMore.set(false);
            this.loading.set(true);
            this.next = null;
        }
        try {
            const result = await this.api.duplicates({ path: this.folderOnly() ? this.folder() : '', after: append ? this.next : null });
            if (current !== this.generation)
                return;
            if (!append) {
                this.first.set(result);
                this.failure.set(null);
            }
            this.canRemove.set(result.canRemove);
            this.permanent.set(result.removesPermanently ?? false);
            this.groups.update(groups => [...groups, ...result.groups]);
            this.next = result.next;
            this.hasMore.set(result.hasMore);
        } catch (error) {
            if (current === this.generation)
                this.failure.set((error as Error).message);
        } finally {
            if (current === this.generation)
                this.loading.set(false);
        }
    }

    protected more(): void {
        void whileBusy(this.loadingMore, () => this.load({ append: true }));
    }

    protected setFolderOnly(only: boolean): void {
        this.folderOnly.set(only);
        writePreference(SCOPE_KEY, only ? '1' : '0');
        void this.load();
    }

    protected onRemoved(group: DuplicateGroup, removed: RemovedCopy): void {
        this.changed.emit();
        this.first.update(first => {
            if (!first?.summary)
                return first;
            const summary = { ...first.summary };
            summary.files -= removed.dissolved ? 2 : 1;
            summary.wastedBytes -= group.size;
            if (removed.dissolved)
                summary.groups--;
            return { ...first, summary };
        });
        if (removed.dissolved)
            this.dissolved.update(keys => new Set([...keys, group.fingerprint + group.size]));
    }

    protected onRestored(group: DuplicateGroup, restored: RestoredCopy): void {
        this.changed.emit();
        this.first.update(first => {
            if (!first?.summary)
                return first;
            const summary = { ...first.summary };
            summary.files += restored.revived ? 2 : 1;
            summary.wastedBytes += group.size;
            if (restored.revived)
                summary.groups++;
            return { ...first, summary };
        });
        if (restored.revived)
            this.dissolved.update(keys => new Set([...keys].filter(key => key !== group.fingerprint + group.size)));
    }
}
