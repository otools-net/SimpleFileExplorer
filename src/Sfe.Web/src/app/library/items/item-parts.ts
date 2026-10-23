import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { LibraryItem } from '../../core/models';
import { HighlightComponent } from '../../shared/highlight.component';
import { IconComponent } from '../../shared/icon.component';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { ListingStore } from '../listing.store';
import { ContextMenuDirective } from '../../menus/context-menu';
import { FolderMenu } from '../folder-menu';
import { ItemActions } from './item-actions.service';

/* The small parts shared by the tiles and rows of the listing. */

/** Added to the hint of a file or folder the index doesn't have yet (shown faded). */
export const NOT_INDEXED = '(not indexed yet: search, tags and duplicates find it after a scan)';

/** Chips of the tags set on a file; clicking one searches for it. */
@Component({
    selector: 'sfe-item-tags',
    imports: [TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'item-tags', '[hidden]': '!item().tags?.length' },
    template: `
        @for (tag of item().tags ?? []; track tag.id) {
            <sfe-tag-chip [tag]="tag" [title]="'Find files tagged ' + tag.displayName" [clickable]="true" (pick)="actions.searchTag($event)" />
        }
    `,
})
export class ItemTagsComponent {
    readonly item = input.required<LibraryItem>();
    protected readonly actions = inject(ItemActions);
}

/** The check box selecting a file or folder. The input covers the box (see styles): a click forwarded by a label might not carry the shift key. */
@Component({
    selector: 'label[sfeSelectBox]',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'select-box', title: 'Select (shift-click selects a range)' },
    template: `
        <input #box type="checkbox" [attr.aria-label]="'Select ' + item().name" [checked]="selected()"
               (click)="store.select(item(), box.checked, $event.shiftKey)">
    `,
})
export class SelectBoxComponent {
    readonly item = input.required<LibraryItem>();
    protected readonly store = inject(ListingStore);
    protected readonly selected = computed(() => this.store.selection().has(this.item().path));
}

/** The button opening the tags of a file (hidden without the right to see tags). */
@Component({
    selector: 'button[sfeTagButton]',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        type: 'button',
        class: 'icon-button small tag-button',
        title: 'Tags',
        '[attr.aria-label]': '"Tags of " + sfeTagButton().name',
        '(click)': 'actions.openTags(sfeTagButton())',
        '[hidden]': '!store.can("tags.view", sfeTagButton())',
    },
    template: `<svg sfeIcon="tag"></svg>`,
})
export class TagButtonComponent {
    readonly sfeTagButton = input.required<LibraryItem>();
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
            <a class="item-folder" routerLink="/" [queryParams]="store.browseParams(item().directory!)" [title]="'Open ' + name()"
               [sfeContextMenu]="folder.linkTarget(item().directory!)">
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
}
