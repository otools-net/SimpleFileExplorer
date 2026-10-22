import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { plural } from '../core/format';
import { FolderDefaults, ScopeTags, TagScope, TagSummary, TagUsage } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Dialogs, registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from './tag-chip.component';
import { TagOption, TagPickerComponent } from './tag-picker.component';

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
 */
@Component({
    selector: 'sfe-scope-tags',
    imports: [BusyDirective, SheetComponent, TagChipComponent, TagPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Tags" [subtitle]="subtitle()" variant="scope-tags">
            @if (!scope().query) {
                <label class="check scope-recursive">
                    <input type="checkbox" [checked]="scope().recursive" [disabled]="applying()" (change)="setRecursive($event)">
                    <span>
                        <strong>Include sub-folders</strong>
                        <span class="field-hint">Also the files of every folder below this one.</span>
                    </span>
                </label>
            }
            @if (error()) {
                <p class="form-error">{{ error() }}</p>
            } @else if (summary(); as summary) {
                <p class="scope-count">{{ countText() }}</p>
                <div class="tag-groups">
                    @if (onAll().length) {
                        <section class="tag-group">
                            <h3>On all these files</h3>
                            <div class="tag-list">
                                @for (entry of onAll(); track entry.tag.id) {
                                    <sfe-tag-chip [tag]="entry.tag" title="Remove this tag from all these files (asks first)"
                                                  [removable]="true" (remove)="askRemove(entry)" />
                                }
                            </div>
                        </section>
                    }
                    @if (onSome().length) {
                        <section class="tag-group">
                            <h3>On some of these files</h3>
                            <div class="tag-list">
                                @for (entry of onSome(); track entry.tag.id) {
                                    <sfe-tag-chip [tag]="entry.tag" [label]="entry.tag.displayName + ' · ' + entry.fileCount.toLocaleString()"
                                                  [title]="'On ' + entry.fileCount.toLocaleString() + ' of ' + filesText() + '. Click to add it to all of them (asks first).'"
                                                  [clickable]="true" [removable]="true" (pick)="askAdd($event)" (remove)="askRemove(entry)" />
                                }
                            </div>
                        </section>
                    }
                    @if (summary.files && !summary.tags.length) {
                        <p class="muted">None of these files has tags yet.</p>
                    }
                </div>
            } @else {
                <div class="tag-loading"><span class="spinner"></span></div>
            }
            @if (pending(); as pending) {
                <div class="scope-confirm" role="group" aria-label="Confirm the change">
                    <p>{{ question() }}</p>
                    @if (hint()) {
                        <p class="field-hint">{{ hint() }}</p>
                    }
                    <div class="scope-confirm-buttons">
                        <button type="button" class="button" title="Change nothing" [disabled]="applying()" (click)="cancel()">Cancel</button>
                        <button type="button" class="button primary" [sfeBusy]="applying()" [disabled]="applying()"
                                [title]="pending.remove ? 'Remove the tag from these files' : 'Add the tag to these files'" (click)="apply()">
                            {{ pending.remove ? 'Remove tag' : 'Add tag' }}
                        </button>
                    </div>
                </div>
            } @else {
                <sfe-tag-picker #picker [placeholder]="scope().query ? 'Add a tag to all the results…' : 'Add a tag to all these files…'"
                                [allowCreate]="true" [exclude]="excluded" [pick]="pick" />
            }
            @if (showDefaults()) {
                <section class="default-tags" aria-labelledby="default-tags-heading">
                    <h3 id="default-tags-heading">Default tags</h3>
                    <p class="field-hint">Every file put into {{ folderName() }} or its sub-folders gets them: scanned, created, uploaded, copied or moved there.</p>
                    @if (defaults(); as defaults) {
                        @if (defaults.own.length || defaults.inherited.length) {
                            <div class="tag-list">
                                @for (tag of defaults.own; track tag.id) {
                                    <sfe-tag-chip [tag]="tag" [removable]="canSetDefaults()" title="A default tag of this folder"
                                                  (remove)="askRemoveDefault($event)" />
                                }
                                @for (entry of defaults.inherited; track entry.tag.id) {
                                    <sfe-tag-chip class="inherited" [tag]="entry.tag" [label]="(entry.tag.displayName || entry.tag.name) + ' · ' + fromName(entry.path)"
                                                  [title]="'A default tag of ' + (entry.path ? '“' + entry.path + '”' : 'the top folder') + ': change it there'" />
                                }
                            </div>
                        } @else {
                            <p class="muted">This folder has no default tags.</p>
                        }
                    } @else if (defaultsError()) {
                        <p class="form-error">{{ defaultsError() }}</p>
                    } @else {
                        <div class="tag-loading"><span class="spinner"></span></div>
                    }
                    @if (pendingDefault(); as pending) {
                        <div class="scope-confirm default-confirm" role="group" aria-label="Confirm the default tag change">
                            <p>{{ defaultQuestion() }}</p>
                            <p class="field-hint">{{ defaultHint() }}</p>
                            <div class="scope-confirm-buttons">
                                <button type="button" class="button" title="Change nothing" [disabled]="savingDefault()" (click)="cancelDefault()">Cancel</button>
                                <button type="button" class="button default-only" [disabled]="savingDefault()" (click)="applyDefault(false)"
                                        [title]="pending.remove ? 'The files already there keep the tag' : 'Only the files put there from now on get it'">
                                    {{ pending.remove ? 'Keep it on the files' : 'New files only' }}
                                </button>
                                @if (canTagFiles()) {
                                    <button type="button" class="button primary default-files" [sfeBusy]="savingDefault()" [disabled]="savingDefault()" (click)="applyDefault(true)"
                                            [title]="pending.remove ? 'Also remove it from the files already there' : 'Also add it to the files already there'">
                                        {{ pending.remove ? 'Remove it from the files too' : 'Existing files too' }}
                                    </button>
                                }
                            </div>
                        </div>
                    } @else if (canSetDefaults() && defaults()) {
                        <sfe-tag-picker #defaultPicker class="default-picker" placeholder="Add a default tag…"
                                        [allowCreate]="true" [exclude]="excludedDefaults" [pick]="pickDefault" />
                    }
                </section>
            }
            <button sheetFooter type="button" class="button" title="Create, edit and delete tags" (click)="dialogs.tagManager()?.open()">Manage tags</button>
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button primary" title="Close (Esc)" (click)="sheet.close()">Done</button>
        </sfe-sheet>
    `,
})
export class ScopeTagsDialog {
    private readonly api = inject(Api);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    protected readonly dialogs = inject(Dialogs);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    private readonly picker = viewChild<TagPickerComponent>('picker');
    private readonly defaultPicker = viewChild<TagPickerComponent>('defaultPicker');

    protected readonly scope = signal<TagScope>({ path: '', recursive: false, query: null });
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
    private readonly folderItem = computed(() => this.store.items().find(item => item.path === this.scope().path) ?? null);
    protected readonly canSetDefaults = computed(() => this.store.can(Rights.folderDefaultTags, this.folderItem()));
    protected readonly canTagFiles = computed(() => this.store.can(Rights.assignTags, this.folderItem()));
    /** Folders only; for those who can't change them, only when there are some to see. */
    protected readonly showDefaults = computed(() => !this.scope().query
        && (this.canSetDefaults() || Boolean(this.defaults()?.own.length || this.defaults()?.inherited.length)));

    /** “Media”, or the root folder. */
    protected readonly folderName = computed(() => this.scope().path ? `“${this.scope().path}”` : 'the top folder');
    /** Where the files are, after "files": "in “Media” and its sub-folders", "found by “cat” in “Media”". */
    private readonly where = computed(() => {
        const { path, recursive, query } = this.scope();
        if (query)
            return `found by “${query}”${path ? ` in “${path}”` : ''}`;
        return `in ${this.folderName()}${recursive ? ' and its sub-folders' : ''}`;
    });
    protected readonly subtitle = computed(() => {
        const { path, query } = this.scope();
        return query ? `Search “${query}”${path ? ` in “${path}”` : ''}` : `Files ${this.where()}`;
    });
    protected readonly filesText = computed(() => plural(this.summary()?.files ?? 0, 'file'));
    protected readonly countText = computed(() => {
        const files = this.summary()?.files ?? 0;
        if (this.scope().query)
            return files ? `${plural(files, 'file')} found.` : 'The search finds no file.';
        return files
            ? `${plural(files, 'indexed file')}. Files not indexed yet are indexed when a tag is added or removed.`
            : 'No file of this folder is indexed yet: they are indexed when a tag is added or removed.';
    });
    protected readonly onAll = computed(() => {
        const summary = this.summary();
        return summary ? summary.tags.filter(entry => entry.fileCount >= summary.files) : [];
    });
    protected readonly onSome = computed(() => {
        const summary = this.summary();
        return summary ? summary.tags.filter(entry => entry.fileCount < summary.files) : [];
    });

    protected readonly question = computed(() => {
        const pending = this.pending();
        if (!pending)
            return '';
        if (pending.remove)
            return `Remove “${pending.name}” from the ${plural(this.usage(pending.tag)?.fileCount ?? 0, 'file')} ${this.where()} that have it?`;
        return this.scope().query
            ? `Add “${pending.name}” to the ${this.filesText()} ${this.where()}?`
            : `Add “${pending.name}” to every file ${this.where()}?`;
    });
    protected readonly hint = computed(() => {
        const pending = this.pending();
        if (!pending || pending.remove)
            return '';
        const having = this.usage(pending.tag)?.fileCount ?? 0;
        return [
            pending.create ? 'The tag is created first.' : '',
            having ? `${plural(having, 'file already has', 'files already have')} it.` : '',
            this.scope().query ? '' : 'This can take a while for a large folder: its files are indexed first.',
        ].filter(Boolean).join(' ');
    });

    protected readonly defaultQuestion = computed(() => {
        const pending = this.pendingDefault();
        if (!pending)
            return '';
        return pending.remove
            ? `Stop giving “${pending.name}” to the files put into ${this.folderName()}?`
            : `Give “${pending.name}” to every file put into ${this.folderName()} or its sub-folders from now on?`;
    });
    protected readonly defaultHint = computed(() => {
        const pending = this.pendingDefault();
        if (!pending)
            return '';
        if (pending.remove)
            return this.canTagFiles()
                ? 'The files already in it and its sub-folders keep it, unless you remove it from them too.'
                : 'The files already there keep it.';
        return [
            pending.create ? 'The tag is created first.' : '',
            this.canTagFiles()
                ? 'The files already in it and its sub-folders can get it now too (they are indexed first, which can take a while for a large folder).'
                : 'The files already there don’t change.',
        ].filter(Boolean).join(' ');
    });

    protected readonly excluded = () => new Set(this.onAll().map(entry => entry.tag.id));
    protected readonly excludedDefaults = () => new Set([...this.defaults()?.own ?? [], ...this.defaults()?.inherited.map(entry => entry.tag) ?? []].map(tag => tag.id));
    /** A tag chosen as a default: asks whether the files already there get it too. */
    protected readonly pickDefault = async (option: TagOption) => {
        this.pendingDefault.set(option.create
            ? { name: option.create, tag: null, create: option.create, remove: false }
            : { name: option.tag!.displayName, tag: option.tag!, remove: false });
    };
    /** A tag chosen in the picker: asks before adding it. */
    protected readonly pick = async (option: TagOption) => {
        this.pending.set(option.create
            ? { name: option.create, tag: null, create: option.create, remove: false }
            : { name: option.tag!.displayName, tag: option.tag!, remove: false });
    };

    constructor() {
        registerDialog(dialogs => dialogs.scopeTags, this);
    }

    /** Opens on the files of a folder (`recursive`: and of its sub-folders) or on a search's matches (`query`). */
    open(scope: TagScope): void {
        this.scope.set({ ...scope, query: scope.query?.trim() || null, recursive: !scope.query && scope.recursive });
        this.pending.set(null);
        this.pendingDefault.set(null);
        this.sheet().open();
        void this.load();
        void this.loadDefaults();
    }

    /** The last part of a parent folder's path, for an inherited default tag's chip. */
    protected fromName(path: string): string {
        return path ? path.slice(path.lastIndexOf('/') + 1) : 'top folder';
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
        if (!pending || this.savingDefault())
            return;
        const path = this.scope().path;
        const folder = this.folderName();
        try {
            await whileBusy(this.savingDefault, async () => {
                let tag = pending.tag;
                if (!tag) {
                    tag = await this.api.createTag({ name: pending.create! });
                    this.toasts.show(`Tag “${tag.name}” created.`);
                    this.pendingDefault.set({ ...pending, tag });
                }
                const id = tag.id;
                const result = await this.api.changeFolderDefaultTags(pending.remove
                    ? { path, remove: [id], removeFromFiles: toFiles }
                    : { path, add: [id], addToFiles: toFiles });
                if (!('defaults' in result)) {
                    this.toasts.error(result.detail ?? 'A scan is running: try again when it has finished.');
                    return;
                }
                this.defaults.set(result.defaults);
                const files = result.files;
                this.toasts.show(pending.remove
                    ? `“${pending.name}” is no longer a default tag of ${folder}` + (files ? `: removed from ${plural(files.removed, 'file')}.` : '; the files keep it.')
                    : `“${pending.name}” is a default tag of ${folder}` + (files ? `: added to ${plural(files.added, 'file')}.` : ': new files there get it.'));
                if (files)
                    this.store.applyScopeTags({ path, recursive: true, query: null }, pending.remove ? { remove: id } : { add: tag });
                this.pendingDefault.set(null);
                // The chips of the folder in the listing and the location bar.
                void this.store.refresh();
                if (files)
                    await this.load();
            });
        } catch (error) {
            this.toasts.error(`Could not change the default tags: ${(error as Error).message}`);
        }
    }

    protected setRecursive(event: Event): void {
        this.scope.update(scope => ({ ...scope, recursive: (event.target as HTMLInputElement).checked }));
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
        if (!pending || this.applying())
            return;
        const scope = this.scope();
        try {
            await whileBusy(this.applying, async () => {
                let tag = pending.tag;
                if (!tag) {
                    tag = await this.api.createTag({ name: pending.create! });
                    this.toasts.show(`Tag “${tag.name}” created.`);
                    this.pending.set({ ...pending, tag });
                }
                const id = tag.id;
                const result = await this.api.changeScopeTags({ ...scope, ...(pending.remove ? { remove: [id] } : { add: [id] }) });
                if (!('files' in result)) {
                    this.toasts.error(result.detail ?? 'A scan is running: try again when it has finished.');
                    return;
                }
                this.toasts.show(pending.remove
                    ? `“${pending.name}” removed from ${plural(result.removed, 'file')}.`
                    : `“${pending.name}” added to ${plural(result.added, 'file')}.`);
                this.store.applyScopeTags(scope, pending.remove ? { remove: id } : { add: tag });
                this.pending.set(null);
                await this.load();
            });
        } catch (error) {
            this.toasts.error(`Could not change the tags: ${(error as Error).message}`);
        }
    }

    private usage(tag: TagSummary | null): TagUsage | undefined {
        return tag ? this.summary()?.tags.find(entry => entry.tag.id === tag.id) : undefined;
    }

    private async loadDefaults(): Promise<void> {
        this.defaultRequests.abort();
        const requests = this.defaultRequests = new AbortController();
        this.defaults.set(null);
        this.defaultsError.set(null);
        if (this.scope().query)
            return;
        try {
            const defaults = await this.api.folderDefaultTags(this.scope().path, requests.signal);
            if (requests === this.defaultRequests)
                this.defaults.set(defaults);
        } catch (error) {
            if (requests === this.defaultRequests)
                this.defaultsError.set(`Could not read the default tags: ${(error as Error).message}`);
        }
    }

    private async load(): Promise<void> {
        this.requests.abort();
        const requests = this.requests = new AbortController();
        this.summary.set(null);
        this.error.set(null);
        try {
            const summary = await this.api.scopeTags(this.scope(), requests.signal);
            if (requests === this.requests)
                this.summary.set(summary);
        } catch (error) {
            if (requests === this.requests)
                this.error.set(`Could not read the tags of these files: ${(error as Error).message}`);
        }
    }
}
