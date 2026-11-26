import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { BEING_WRITTEN, BEING_WRITTEN_TITLE, isPartial } from '../../files/partial-files';
import { percentOf, WritingProgress } from '../../files/writing-progress';
import { formatSize } from '../../core/format';
import { LibraryItem } from '../../core/models';
import { HighlightComponent } from '../../shared/highlight.component';
import { IconComponent } from '../../shared/icon.component';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { TagMenu } from '../../tags/tag-menu';
import { ListingStore } from '../listing.store';
import { ContextMenuDirective } from '../../menus/context-menu';
import { FolderMenu } from '../folder-menu';
import { ItemActions } from './item-actions.service';
import { itemLock } from '../read-only';

/* The small parts shared by the tiles and rows of the listing. */

/** Added to the hint of a file or folder the index doesn't have yet (shown faded). */
export const NOT_INDEXED = $localize`Not indexed yet: search, tags and duplicates find it after a scan.`;

/**
 * The mark of a file or folder still being written (`isPartial`), with a bar showing how far it is while the change
 * feed tells it (`WritingProgress`, the percentage in the hint); nothing otherwise.
 */
@Component({
    selector: 'sfe-partial-mark',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { style: 'display: contents' },
    template: `
        @if (partial()) {
            <span class="partial-mark" [title]="title()">{{ label }}</span>
            @if (percent() !== null) {
                <progress class="write-progress" max="100" [value]="percent()" [title]="title()"
                          [attr.aria-label]="progressLabel()"></progress>
            }
        }
    `,
})
export class PartialMarkComponent {
    readonly item = input.required<LibraryItem>();
    private readonly writing = inject(WritingProgress);
    protected readonly partial = computed(() => isPartial(this.item()));
    private readonly progress = computed(() => (this.partial() ? this.writing.of(this.item().path) : undefined));
    protected readonly percent = computed(() => {
        const progress = this.progress();
        return progress ? percentOf(progress) : null;
    });
    protected readonly label = BEING_WRITTEN;
    /** How far it is (written of its size), then what the mark means. */
    protected readonly title = computed(() => {
        const progress = this.progress();
        if (!progress) return BEING_WRITTEN_TITLE;
        const [done, total] = [formatSize(progress.bytesDone), formatSize(progress.bytesTotal)];
        const written =
            progress.bytesTotal > 0
                ? $localize`${this.percent()}:percent: % written (${done}:done: of ${total}:total:).`
                : $localize`${done}:done: written.`;
        return `${written} ${BEING_WRITTEN_TITLE}`;
    });
    protected readonly progressLabel = computed(() => $localize`${this.percent()}:percent: % written`);
}

/** Chips of the tags set on a file; clicking one searches for it. */
@Component({
    selector: 'sfe-item-tags',
    imports: [TagChipComponent, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'item-tags', '[hidden]': '!item().tags?.length' },
    template: `
        @for (tag of item().tags ?? []; track tag.id) {
            <sfe-tag-chip
                [tag]="tag"
                [title]="findTitle(tag.displayName)"
                [clickable]="true"
                (pick)="actions.searchTag($event)"
                [sfeContextMenu]="tagMenu.target(tag, item())"
            />
        }
    `,
})
export class ItemTagsComponent {
    readonly item = input.required<LibraryItem>();
    protected readonly actions = inject(ItemActions);
    protected readonly tagMenu = inject(TagMenu);

    protected findTitle(tag: string): string {
        return $localize`Find the files tagged “${tag}:tag:”.`;
    }
}

/**
 * The check box selecting a file or folder. The input covers the box (see styles): a click forwarded by a label might
 * not carry the shift key.
 */
@Component({
    selector: 'label[sfeSelectBox]',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'select-box', '[title]': 'hint' },
    template: `
        <input #box type="checkbox" [attr.aria-label]="label()" [checked]="selected()"
               (click)="store.select(item(), box.checked, $event.shiftKey)">
    `,
})
export class SelectBoxComponent {
    readonly item = input.required<LibraryItem>();
    protected readonly store = inject(ListingStore);
    protected readonly selected = computed(() => this.store.selection().has(this.item().path));
    protected readonly hint = $localize`Select it (Shift-click selects a range).`;
    protected readonly label = computed(() => $localize`Select “${this.item().name}:name:”`);
}

/** The button opening the tags of a file (hidden without the right to see tags). */
@Component({
    selector: 'button[sfeTagButton]',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        type: 'button',
        class: 'icon-button small tag-button',
        '[title]': 'hint',
        '[attr.aria-label]': 'label()',
        '(click)': 'actions.openTags(sfeTagButton())',
        '[hidden]': '!store.can("tags.view", sfeTagButton())',
    },
    template: `<svg sfeIcon="tag"></svg>`,
})
export class TagButtonComponent {
    readonly sfeTagButton = input.required<LibraryItem>();
    protected readonly hint = $localize`See or change the tags of this file.`;
    protected readonly label = computed(() => $localize`Tags of “${this.sfeTagButton().name}:name:”`);
    protected readonly actions = inject(ItemActions);
    protected readonly store = inject(ListingStore);
}

/** The folder of a search result, linking to it. */
@Component({
    selector: 'sfe-item-folder',
    imports: [HighlightComponent, RouterLink, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { style: 'display: contents' },
    template: `
        @if (item().directory !== undefined && item().directory !== null) {
            <a
                class="item-folder"
                routerLink="/"
                [queryParams]="store.browseParams(item().directory!)"
                [title]="title()"
                [sfeContextMenu]="folder.linkTarget(item().directory!)"
            >
                <sfe-highlight [text]="name()" [pattern]="pattern()" />
            </a>
        }
    `,
})
export class ItemFolderComponent {
    readonly item = input.required<LibraryItem>();
    readonly pattern = input<RegExp | null>(null);
    protected readonly store = inject(ListingStore);
    protected readonly folder = inject(FolderMenu);
    private readonly config = inject(ConfigService);
    protected readonly name = computed(() => this.item().directory || this.config.config()?.rootName || '');
    protected readonly title = computed(() => $localize`Open the folder “${this.name()}:name:”.`);
}

/**
 * A lock on a file or folder the session can't change while it can change its folder (`itemLock`): its hint says
 * why. Nothing otherwise (agents/docs/features/item-access.md).
 */
@Component({
    selector: 'sfe-lock-mark',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { style: 'display: contents' },
    template: `
        @if (lock(); as lock) {
            <span class="lock-mark" role="img" [title]="lock" [attr.aria-label]="lock"><svg sfeIcon="lock"></svg></span>
        }
    `,
})
export class LockMarkComponent {
    readonly item = input.required<LibraryItem>();
    private readonly store = inject(ListingStore);
    protected readonly lock = computed(() => itemLock(this.store.listing(), this.item()));
}
