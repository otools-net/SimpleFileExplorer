import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import {
    appliedOf,
    countOf,
    defaultChangedOf,
    defaultHintOf,
    defaultQuestionOf,
    folderNameOf,
    hintOf,
    inheritedTitleOf,
    questionOf,
    SCOPE_TAG_TEXTS,
    someTitleOf,
    subtitleOf,
    whereOf,
} from './scope-tags-texts';
import { FolderDefaults, TagScope, TagSummary, TagUsage } from '../core/tag.models';
import { ScopeTags } from './tag.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Dialogs, registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from './tag-chip.component';
import { TagOption, TagPickerComponent } from './tag-picker.component';
import { scanRunning, tagCreated } from './tags';

/** A change waiting for the user's confirmation: a tag to add (`create`: a new tag, made on confirming) or remove. */
interface PendingChange {
    name: string;
    tag: TagSummary | null;
    create?: string;
    remove: boolean;
}

/**
 * The tags of every file of a folder (optionally with its sub-folders), or of every match of a search, without
 * loading them: the server counts the tags on them and changes them all at once. Such a change can touch thousands of
 * files, so each one is described and waits for a confirmation.
 *
 * For a folder, also its default tags (agents/docs/features/default-tags.md): the tags every file put into it or its
 * sub-folders gets. Adding or removing one asks whether the files already there change too.
 *
 * In a connection (`config.place()` when opened), the files are those the library's index of it holds: nothing is
 * indexed for the change, and folders have no default tags there.
 */
@Component({
    selector: 'sfe-scope-tags',
    imports: [BusyDirective, SheetComponent, TagChipComponent, TagPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './scope-tags.dialog.html',
})
export class ScopeTagsDialog {
    private readonly api = inject(Api);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    private readonly config = inject(ConfigService);
    protected readonly dialogs = inject(Dialogs);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    private readonly picker = viewChild<TagPickerComponent>('picker');
    private readonly defaultPicker = viewChild<TagPickerComponent>('defaultPicker');

    protected readonly scope = signal<TagScope>({ path: '', recursive: false, query: null });
    /** The connection whose files are tagged; null for the library. */
    private readonly connection = signal<number | null>(null);
    protected readonly summary = signal<ScopeTags | null>(null);
    protected readonly error = signal<string | null>(null);
    protected readonly pending = signal<PendingChange | null>(null);
    protected readonly applying = signal(false);
    private requests = new AbortController();

    protected readonly defaults = signal<FolderDefaults | null>(null);
    protected readonly defaultsError = signal<string | null>(null);
    protected readonly pendingDefault = signal<PendingChange | null>(null);
    protected readonly savingDefault = signal(false);
    private defaultRequests = new AbortController();

    /** The folder's item when the listing has it (its folder rules), else null: the folder listed. */
    private readonly folderItem = computed(
        () => this.store.items().find((item) => item.path === this.scope().path) ?? null,
    );
    protected readonly canSetDefaults = computed(() => this.store.can(Rights.folderDefaultTags, this.folderItem()));
    protected readonly canTagFiles = computed(() => this.store.can(Rights.assignTags, this.folderItem()));
    /** Folders only; for those who can't change them, only when there are some to see. */
    protected readonly showDefaults = computed(
        () =>
            !this.scope().query &&
            (this.canSetDefaults() || Boolean(this.defaults()?.own.length || this.defaults()?.inherited.length)),
    );

    protected readonly folderName = computed(() => folderNameOf(this.scope().path));
    private readonly where = computed(() => whereOf(this.scope()));
    protected readonly subtitle = computed(() => subtitleOf(this.scope()));
    protected readonly countText = computed(() =>
        countOf(this.summary()?.files ?? 0, Boolean(this.scope().query), Boolean(this.connection())),
    );
    protected readonly onAll = computed(() => {
        const summary = this.summary();
        return summary ? summary.tags.filter((entry) => entry.fileCount >= summary.files) : [];
    });
    protected readonly onSome = computed(() => {
        const summary = this.summary();
        return summary ? summary.tags.filter((entry) => entry.fileCount < summary.files) : [];
    });

    protected readonly question = computed(() => {
        const pending = this.pending();
        if (!pending) return '';
        const count = pending.remove ? (this.usage(pending.tag)?.fileCount ?? 0) : (this.summary()?.files ?? 0);
        return questionOf(pending.name, pending.remove, this.where(), count, Boolean(this.scope().query));
    });
    protected readonly hint = computed(() => {
        const pending = this.pending();
        if (!pending || pending.remove) return '';
        const having = this.usage(pending.tag)?.fileCount ?? 0;
        return hintOf(Boolean(pending.create), having, !this.scope().query && !this.connection());
    });

    protected readonly defaultQuestion = computed(() => {
        const pending = this.pendingDefault();
        return pending ? defaultQuestionOf(pending.name, pending.remove, this.folderName()) : '';
    });
    protected readonly defaultHint = computed(() => {
        const pending = this.pendingDefault();
        return pending ? defaultHintOf(Boolean(pending.create), pending.remove, this.canTagFiles()) : '';
    });
    protected readonly pickerPlaceholder = computed(() =>
        this.scope().query ? $localize`Add a tag to all the results…` : $localize`Add a tag to all these files…`,
    );
    protected readonly texts = SCOPE_TAG_TEXTS;

    protected someTitle(entry: TagUsage): string {
        return someTitleOf(entry.fileCount, this.summary()?.files ?? 0);
    }

    protected inheritedTitle(path: string): string {
        return inheritedTitleOf(path);
    }

    protected readonly excluded = () => new Set(this.onAll().map((entry) => entry.tag.id));
    protected readonly excludedDefaults = () =>
        new Set(
            [...(this.defaults()?.own ?? []), ...(this.defaults()?.inherited.map((entry) => entry.tag) ?? [])].map(
                (tag) => tag.id,
            ),
        );
    /** A tag chosen as a default: asks whether the files already there get it too. */
    protected readonly pickDefault = async (option: TagOption) => {
        this.pendingDefault.set(
            option.create
                ? { name: option.create, tag: null, create: option.create, remove: false }
                : { name: option.tag!.displayName, tag: option.tag!, remove: false },
        );
    };
    /** A tag chosen in the picker: asks before adding it. */
    protected readonly pick = async (option: TagOption) => {
        this.pending.set(
            option.create
                ? { name: option.create, tag: null, create: option.create, remove: false }
                : { name: option.tag!.displayName, tag: option.tag!, remove: false },
        );
    };

    constructor() {
        registerDialog((dialogs) => dialogs.scopeTags, this);
    }

    /** Opens on the files of a folder (`recursive`: and of its sub-folders) or on a search's matches (`query`). */
    open(scope: TagScope): void {
        this.scope.set({ ...scope, query: scope.query?.trim() || null, recursive: !scope.query && scope.recursive });
        this.connection.set(this.config.place()?.id ?? null);
        this.pending.set(null);
        this.pendingDefault.set(null);
        this.sheet().open();
        void this.load();
        void this.loadDefaults();
    }

    /** The last part of a parent folder's path, for an inherited default tag's chip. */
    protected fromName(path: string): string {
        return path ? path.slice(path.lastIndexOf('/') + 1) : $localize`:the library's own folder, short:library`;
    }

    protected askRemoveDefault(tag: TagSummary): void {
        this.pendingDefault.set({ name: tag.displayName || tag.name, tag, remove: true });
    }

    protected cancelDefault(): void {
        this.pendingDefault.set(null);
        setTimeout(() => this.defaultPicker()?.focus());
    }

    /** Applies the confirmed default tag change; `toFiles`: the files already in the folder tree change too. */
    protected async applyDefault(toFiles: boolean): Promise<void> {
        const pending = this.pendingDefault();
        if (!pending || this.savingDefault()) return;
        const path = this.scope().path;
        const folder = this.folderName();
        try {
            await whileBusy(this.savingDefault, async () => {
                let tag = pending.tag;
                if (!tag) {
                    tag = await this.api.createTag({ name: pending.create! });
                    this.toasts.show(tagCreated(tag.name));
                    this.pendingDefault.set({ ...pending, tag });
                }
                const id = tag.id;
                const result = await this.api.changeFolderDefaultTags(
                    pending.remove
                        ? { path, remove: [id], removeFromFiles: toFiles }
                        : { path, add: [id], addToFiles: toFiles },
                );
                if (!('defaults' in result)) {
                    const reason = result.detail ?? scanRunning();
                    this.toasts.error($localize`Could not change the default tags: ${reason}:reason:`);
                    return;
                }
                this.defaults.set(result.defaults);
                const files = result.files;
                this.toasts.show(defaultChangedOf(pending.name, pending.remove, folder, files));
                if (files)
                    this.store.applyScopeTags(
                        { path, recursive: true, query: null },
                        pending.remove ? { remove: id } : { add: tag },
                    );
                this.pendingDefault.set(null);
                // The chips of the folder in the listing and the location bar.
                void this.store.refresh();
                if (files) await this.load();
            });
        } catch (error) {
            this.toasts.error($localize`Could not change the default tags: ${(error as Error).message}:reason:`);
        }
    }

    protected setRecursive(event: Event): void {
        this.scope.update((scope) => ({ ...scope, recursive: (event.target as HTMLInputElement).checked }));
        this.pending.set(null);
        void this.load();
    }

    protected askAdd(tag: TagSummary): void {
        this.pending.set({ name: tag.displayName, tag, remove: false });
    }

    protected askRemove(entry: TagUsage): void {
        this.pending.set({ name: entry.tag.displayName, tag: entry.tag, remove: true });
    }

    protected cancel(): void {
        this.pending.set(null);
        setTimeout(() => this.picker()?.focus());
    }

    /** Applies the confirmed change to every file of the scope, then counts again. */
    protected async apply(): Promise<void> {
        const pending = this.pending();
        if (!pending || this.applying()) return;
        const scope = this.scope();
        try {
            await whileBusy(this.applying, async () => {
                let tag = pending.tag;
                if (!tag) {
                    tag = await this.api.createTag({ name: pending.create! });
                    this.toasts.show(tagCreated(tag.name));
                    this.pending.set({ ...pending, tag });
                }
                const id = tag.id;
                const change = { ...scope, ...(pending.remove ? { remove: [id] } : { add: [id] }) };
                const result = await this.api.changeScopeTags(change, this.connection());
                if (!('files' in result)) {
                    this.toasts.error($localize`Could not change the tags: ${result.detail ?? scanRunning()}:reason:`);
                    return;
                }
                this.toasts.show(
                    appliedOf(pending.name, pending.remove, pending.remove ? result.removed : result.added),
                );
                this.store.applyScopeTags(scope, pending.remove ? { remove: id } : { add: tag });
                this.pending.set(null);
                await this.load();
            });
        } catch (error) {
            this.toasts.error($localize`Could not change the tags: ${(error as Error).message}:reason:`);
        }
    }

    private usage(tag: TagSummary | null): TagUsage | undefined {
        return tag ? this.summary()?.tags.find((entry) => entry.tag.id === tag.id) : undefined;
    }

    private async loadDefaults(): Promise<void> {
        this.defaultRequests.abort();
        const requests = (this.defaultRequests = new AbortController());
        this.defaults.set(null);
        this.defaultsError.set(null);
        if (this.scope().query || this.connection()) return;
        try {
            const defaults = await this.api.folderDefaultTags(this.scope().path, requests.signal);
            if (requests === this.defaultRequests) this.defaults.set(defaults);
        } catch (error) {
            if (requests === this.defaultRequests) this.defaultsError.set((error as Error).message);
        }
    }

    private async load(): Promise<void> {
        this.requests.abort();
        const requests = (this.requests = new AbortController());
        this.summary.set(null);
        this.error.set(null);
        try {
            const summary = await this.api.scopeTags(this.scope(), requests.signal, this.connection());
            if (requests === this.requests) this.summary.set(summary);
        } catch (error) {
            if (requests === this.requests) this.error.set((error as Error).message);
        }
    }
}
