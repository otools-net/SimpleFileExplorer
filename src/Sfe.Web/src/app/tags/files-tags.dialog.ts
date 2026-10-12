import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { plural } from '../core/format';
import { FileTagChange, LibraryItem, TagSummary } from '../core/models';
import { ToastService } from '../core/toast.service';
import { Dialogs, registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from './tag-chip.component';
import { TagOption, TagPickerComponent } from './tag-picker.component';

interface TagCount {
    tag: TagSummary;
    count: number;
}

/**
 * The tags of several files: shows the tags they have (on all of them, or on some), and adds or removes tags on all
 * of them at once. The server takes `config.maxFilesPerTagChange` files per request: larger selections are sent in
 * parts.
 */
@Component({
    selector: 'sfe-files-tags',
    imports: [SheetComponent, TagChipComponent, TagPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Tags" [subtitle]="subtitle()">
            <div class="tag-groups">
                @if (onAll().length) {
                    <section class="tag-group">
                        <h3>On all these files</h3>
                        <div class="tag-list">
                            @for (entry of onAll(); track entry.tag.id) {
                                <sfe-tag-chip [tag]="entry.tag" title="Edit this tag" [clickable]="true" [removable]="true" (pick)="edit($event)" (remove)="remove($event)" />
                            }
                        </div>
                    </section>
                }
                @if (onSome().length) {
                    <section class="tag-group">
                        <h3>On some of these files</h3>
                        <div class="tag-list">
                            @for (entry of onSome(); track entry.tag.id) {
                                <sfe-tag-chip [tag]="entry.tag" [label]="entry.tag.displayName + ' · ' + entry.count.toLocaleString()"
                                              [title]="'On ' + entry.count.toLocaleString() + ' of ' + filesText() + '. Click to add it to all of them.'"
                                              [clickable]="true" [removable]="true" (pick)="addToAll($event)" (remove)="remove($event)" />
                            }
                        </div>
                    </section>
                }
                @if (!onAll().length && !onSome().length) {
                    <p class="muted">None of these files has tags yet. A tag added below goes on all of them.</p>
                }
            </div>
            <div class="tag-loading" [hidden]="!progress()"><span class="spinner"></span></div>
            <sfe-tag-picker #picker placeholder="Add a tag to all these files…" [allowCreate]="true" [exclude]="excluded" [pick]="add" />
            <button sheetFooter type="button" class="button" title="Create, edit and delete tags" (click)="dialogs.tagManager()?.open()">Manage tags</button>
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button primary" title="Close (Esc)" (click)="sheet.close()">Done</button>
        </sfe-sheet>
    `,
})
export class FilesTagsDialog {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    protected readonly dialogs = inject(Dialogs);
    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    private readonly picker = viewChild.required<TagPickerComponent>('picker');

    /** The selected files, with their tags kept up to date. */
    private readonly items = signal<LibraryItem[]>([]);
    protected readonly progress = signal(false);
    private busy = false;

    protected readonly subtitle = computed(() => plural(this.items().length, 'file'));
    protected readonly filesText = this.subtitle;
    /** Each tag set on at least one of the files, with the number of files it is on; most common first. */
    private readonly counts = computed((): TagCount[] => {
        const byId = new Map<number, TagCount>();
        for (const item of this.items()) {
            for (const tag of item.tags ?? []) {
                const entry = byId.get(tag.id);
                if (entry)
                    entry.count++;
                else
                    byId.set(tag.id, { tag, count: 1 });
            }
        }
        return [...byId.values()].sort((a, b) => b.count - a.count
            || a.tag.displayName.localeCompare(b.tag.displayName, undefined, { sensitivity: 'base' }));
    });
    protected readonly onAll = computed(() => this.counts().filter(entry => entry.count === this.items().length));
    protected readonly onSome = computed(() => this.counts().filter(entry => entry.count < this.items().length));

    protected readonly excluded = () => new Set(this.onAll().map(entry => entry.tag.id));
    protected readonly add = async (option: TagOption) => {
        const tag = option.create ? await this.api.createTag({ name: option.create }) : option.tag!;
        if (option.create)
            this.toasts.show(`Tag “${tag.name}” created.`);
        await this.change({ add: [tag.id] });
    };

    constructor() {
        registerDialog(dialogs => dialogs.filesTags, this);
    }

    open(selected: LibraryItem[]): void {
        this.items.set(selected);
        this.sheet().open();
        this.picker().focus();
    }

    protected addToAll(tag: TagSummary): void {
        this.report(this.change({ add: [tag.id] }));
    }

    protected remove(tag: TagSummary): void {
        const paths = this.items().filter(item => item.tags?.some(t => t.id === tag.id)).map(item => item.path);
        this.report(this.change({ remove: [tag.id] }, paths));
    }

    protected async edit(tag: TagSummary): Promise<void> {
        // The store updates the listed items' tags when a tag is edited or deleted.
        await this.dialogs.tagEditor()?.open(tag.id);
        const listed = new Map(this.store.items().map(item => [item.path, item]));
        this.items.update(items => items.map(item => listed.get(item.path) ?? item));
    }

    private report(promise: Promise<void>): void {
        promise.catch((error: Error) => this.toasts.error(error.message));
    }

    /**
     * Applies `delta` to `paths`, `maxFilesPerTagChange` files per request. When a request fails, the parts sent
     * before it stay applied (and shown).
     */
    private async change(delta: Omit<FileTagChange, 'paths'>, paths = this.items().map(item => item.path)): Promise<void> {
        if (this.busy)
            return;
        this.busy = true;
        const size = this.config.value.maxFilesPerTagChange || 1000;
        this.progress.set(paths.length > size);
        try {
            for (let start = 0; start < paths.length; start += size) {
                const result = await this.api.changeFileTags({ paths: paths.slice(start, start + size), ...delta });
                const byPath = new Map(result.files.map(file => [file.path, file.tags]));
                this.items.update(items => items.map(item => byPath.has(item.path) ? { ...item, tags: byPath.get(item.path) } : item));
                this.store.applyFileTags(result.files);
            }
        } finally {
            this.busy = false;
            this.progress.set(false);
        }
    }
}
