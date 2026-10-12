import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { FileTags, LibraryItem, TagSummary } from '../core/models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Dialogs, registerDialog } from '../library/dialogs.service';
import { ListingStore } from '../library/listing.store';
import { SheetComponent } from '../shared/sheet.component';
import { TagChipComponent } from './tag-chip.component';
import { TagOption, TagPickerComponent } from './tag-picker.component';

/** The tags of a file, grouped by category: add (or create), remove, or edit them, each with its right (only seen without them). */
@Component({
    selector: 'sfe-file-tags',
    imports: [SheetComponent, TagChipComponent, TagPickerComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <sfe-sheet #sheet heading="Tags" [subtitle]="name()">
            <div class="tag-groups">
                @if (!current()) {
                    <div class="tag-loading"><span class="spinner"></span></div>
                } @else if (!current()!.tags.length) {
                    <p class="muted">No tags yet. Add one below: tags inherit from their parent tags, so a few well-chosen tags go a long way.</p>
                } @else {
                    @for (group of current()!.groups; track $index) {
                        <section class="tag-group">
                            <h3>{{ group.category ? group.category.displayName : 'Tags' }}</h3>
                            <div class="tag-list">
                                @for (tag of group.tags; track tag.id) {
                                    <sfe-tag-chip [tag]="tag" [title]="canManage() ? 'Edit this tag' : tag.displayName" [clickable]="canManage()" [removable]="canAssign()" (pick)="edit($event)" (remove)="remove($event)" />
                                }
                            </div>
                        </section>
                    }
                }
            </div>
            <sfe-tag-picker #picker placeholder="Add a tag…" [hidden]="!canAssign()" [allowCreate]="canManage()" [exclude]="excluded" [pick]="add" />
            @if (canManage()) {
                <button sheetFooter type="button" class="button" title="Create, edit and delete tags" (click)="dialogs.tagManager()?.open()">Manage tags</button>
            }
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button primary" title="Close (Esc)" (click)="sheet.close()">Done</button>
        </sfe-sheet>
    `,
})
export class FileTagsDialog {
    private readonly api = inject(Api);
    private readonly store = inject(ListingStore);
    private readonly toasts = inject(ToastService);
    protected readonly dialogs = inject(Dialogs);
    private readonly config = inject(ConfigService);
    protected readonly canAssign = computed(() => this.config.rights().has(Rights.assignTags));
    protected readonly canManage = computed(() => this.config.rights().has(Rights.manageTags));
    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    private readonly picker = viewChild.required<TagPickerComponent>('picker');

    private path = '';
    protected readonly name = signal('');
    /** Null while loading. */
    protected readonly current = signal<FileTags | null>(null);

    protected readonly excluded = () => new Set(this.current()?.tags.map(tag => tag.id));
    protected readonly add = async (option: TagOption) => {
        const tag = option.create ? await this.api.createTag({ name: option.create }) : option.tag!;
        if (option.create)
            this.toasts.show(`Tag “${tag.name}” created.`);
        await this.change({ add: [tag.id] });
    };

    constructor() {
        registerDialog(dialogs => dialogs.fileTags, this);
    }

    async open(item: LibraryItem): Promise<void> {
        this.path = item.path;
        this.name.set(item.name);
        this.current.set(null);
        this.sheet().open();
        if (this.canAssign())
            this.picker().focus();
        await this.reload();
    }

    protected remove(tag: TagSummary): void {
        this.change({ remove: [tag.id] }).catch((error: Error) => this.toasts.error(error.message));
    }

    protected async edit(tag: TagSummary): Promise<void> {
        await this.dialogs.tagEditor()?.open(tag.id);
        if (this.sheet().isOpen())
            await this.reload();
    }

    private async change(delta: { add?: number[]; remove?: number[] }): Promise<void> {
        const result = await this.api.changeFileTags({ paths: [this.path], ...delta });
        this.current.set(result.files[0]);
        this.store.applyFileTags(result.files);
    }

    private async reload(): Promise<void> {
        try {
            this.current.set(await this.api.fileTags(this.path));
        } catch (error) {
            this.toasts.error(`Could not load the tags: ${(error as Error).message}`);
        }
    }
}
