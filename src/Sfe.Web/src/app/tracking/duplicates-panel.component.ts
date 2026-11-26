import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { formatSize } from '../core/format';
import { icu } from '../core/i18n/icu';
import { isFileConflict, LibraryItem } from '../core/models';
import { FileTags } from '../core/tag.models';
import { DuplicateGroup, DuplicateResults } from './tracking.models';
import { readPreference, writePreference } from '../core/preferences';
import { ToastService } from '../core/toast.service';
import { UndoHistory } from '../core/undo-history';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { checkedOf, valueOf } from '../shared/events';
import { StatusBoxComponent, TrackingFileComponent } from './tracking-parts';
import { duplicatesStatus, moreFiles, notPutBack, removedText } from './tracking-texts';

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
    templateUrl: './duplicate-group.component.html',
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
    private readonly undos = inject(UndoHistory);
    private readonly removedPaths = signal<ReadonlySet<string>>(new Set());
    private readonly keptTags = signal<ReadonlyMap<string, FileTags['tags']>>(new Map());
    protected readonly confirming = signal<string | null>(null);
    protected readonly keep = signal('');
    protected readonly removing = signal(false);
    protected readonly valueOf = valueOf;
    private destroyed = false;

    constructor() {
        // An Undo can come after the panel was reloaded.
        inject(DestroyRef).onDestroy(() => (this.destroyed = true));
    }

    protected readonly files = computed(() =>
        this.group()
            .files.filter((file) => !this.removedPaths().has(file.path))
            .map((file) => (this.keptTags().has(file.path) ? { ...file, tags: this.keptTags().get(file.path) } : file)),
    );
    private readonly count = computed(() => this.group().count - this.removedPaths().size);
    protected readonly hidden = computed(() => this.count() - this.files().length);
    protected readonly countText = computed(() =>
        icu($localize`{count, plural, one {# copy} other {# copies}}`, { count: this.count() }),
    );
    protected readonly sizeText = computed(() => {
        const each = formatSize(this.group().size);
        const extra = formatSize(this.group().size * (this.count() - 1));
        return $localize`${each}:size: each · ${extra}:extra: extra`;
    });
    protected readonly hiddenText = computed(() => moreFiles(this.hidden()));
    protected readonly removeTitle = computed(() =>
        this.permanent()
            ? $localize`Delete this copy for good, keeping another one (asks first).`
            : $localize`Move this copy to the trash, keeping another one (asks first).`,
    );
    protected readonly confirmTitle = computed(() =>
        this.permanent()
            ? $localize`Delete this file from the disk now. It can't be put back.`
            : $localize`Move this file to the trash now. It can be put back from there.`,
    );

    protected confirmLabel(file: LibraryItem): string {
        return $localize`Remove “${file.name}:name:”`;
    }

    protected othersOf(file: LibraryItem): LibraryItem[] {
        return this.files().filter((other) => other.path !== file.path);
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
            this.removedPaths.update((paths) => new Set([...paths, file.path]));
            this.keptTags.update((tags) => new Map(tags).set(keepPath, result.kept.tags));
            const trashId = result.trashId;
            const trashed = typeof trashId === 'number';
            const text = removedText(file.name, trashed, keepPath, result.kept.tags.length > 0);
            if (trashed)
                this.toasts.success(text, {
                    timeout: 10000,
                    action: this.undos.add({
                        done: text,
                        hint: $localize`Put it back where it was.`,
                        run: () => this.undo(file, trashId),
                    }),
                });
            else this.toasts.success(text);
            this.removed.emit({ file, keepPath, kept: result.kept, dissolved: this.count() < 2 });
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error($localize`Could not remove “${file.name}:name:”: ${reason}:reason:`, 8000);
        }
    }

    /** Puts a removed copy back from the trash; the tags it gave to the copy kept stay there. */
    private async undo(file: LibraryItem, trashId: number): Promise<void> {
        try {
            const result = await this.api.restoreFromTrash(trashId);
            if (isFileConflict(result)) {
                this.toasts.error(notPutBack(file.name, result.detail ?? null));
                return;
            }
        } catch (error) {
            this.toasts.error(notPutBack(file.name, (error as Error).message));
            return;
        }
        this.toasts.success($localize`“${file.name}:name:” is back.`);
        if (this.destroyed || !this.removedPaths().has(file.path)) return;
        const revived = this.count() < 2;
        this.removedPaths.update((paths) => new Set([...paths].filter((path) => path !== file.path)));
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
        <p class="settings-intro" i18n>Files of the index with the same size and the same fingerprint, the biggest
        first. Only indexed files are compared: scan a folder to include its latest files.</p>
        @if (folder()) {
            <label class="check tracking-scope">
                <input type="checkbox" [checked]="folderOnly()" (change)="setFolderOnly(checkedOf($event))">
                <span i18n>Only in “{{ folder() }}”</span>
            </label>
        }
        <sfe-status-box [state]="status().state" [text]="status().text" [detail]="status().detail" />
        <div class="tracking-groups">
            @if (loading()) {
                <div class="tag-loading"><span class="spinner"></span></div>
            }
            @for (group of groups(); track group.fingerprint + group.size) {
                <!-- Hidden rather than dropped: an Undo brings it back. -->
                <section
                    [sfeDuplicateGroup]="group"
                    [canRemove]="canRemove()"
                    [permanent]="permanent()"
                    [hidden]="dissolved().has(group.fingerprint + group.size)"
                    (openFolder)="openFolder.emit($event)"
                    (removed)="onRemoved(group, $event)"
                    (restored)="onRestored(group, $event)"
                ></section>
            }
        </div>
        <button
            type="button"
            class="button"
            title="Show the next groups."
            i18n-title
            [hidden]="!hasMore()"
            [sfeBusy]="loadingMore()"
            [disabled]="loadingMore()"
            (click)="more()"
            i18n
        >Load more</button>
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

    protected readonly status = computed(() => duplicatesStatus(this.first(), this.failure()));
    /** For the tab. */
    readonly badge = computed(() =>
        this.first()?.summary?.groups ? this.first()!.summary!.groups.toLocaleString() : '',
    );

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
            const result = await this.api.duplicates({
                path: this.folderOnly() ? this.folder() : '',
                after: append ? this.next : null,
            });
            if (current !== this.generation) return;
            if (!append) {
                this.first.set(result);
                this.failure.set(null);
            }
            this.canRemove.set(result.canRemove);
            this.permanent.set(result.removesPermanently ?? false);
            this.groups.update((groups) => [...groups, ...result.groups]);
            this.next = result.next;
            this.hasMore.set(result.hasMore);
        } catch (error) {
            if (current === this.generation) this.failure.set((error as Error).message);
        } finally {
            if (current === this.generation) this.loading.set(false);
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
        this.first.update((first) => {
            if (!first?.summary) return first;
            const summary = { ...first.summary };
            summary.files -= removed.dissolved ? 2 : 1;
            summary.wastedBytes -= group.size;
            if (removed.dissolved) summary.groups--;
            return { ...first, summary };
        });
        if (removed.dissolved) this.dissolved.update((keys) => new Set([...keys, group.fingerprint + group.size]));
    }

    protected onRestored(group: DuplicateGroup, restored: RestoredCopy): void {
        this.changed.emit();
        this.first.update((first) => {
            if (!first?.summary) return first;
            const summary = { ...first.summary };
            summary.files += restored.revived ? 2 : 1;
            summary.wastedBytes += group.size;
            if (restored.revived) summary.groups++;
            return { ...first, summary };
        });
        if (restored.revived)
            this.dissolved.update((keys) => new Set([...keys].filter((key) => key !== group.fingerprint + group.size)));
    }
}
