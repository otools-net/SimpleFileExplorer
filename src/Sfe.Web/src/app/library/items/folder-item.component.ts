import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../../core/config.service';
import { CopyPathService } from '../../core/copy-path.service';
import { LibraryItem } from '../../core/models';
import { Rights } from '../../core/rights';
import { IconComponent } from '../../shared/icon.component';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { FolderCoverComponent } from './folder-cover.component';
import { CopyPathButtonComponent, NOT_INDEXED } from './item-parts';

/**
 * A listed folder: a card with its first pictures (`cards`), or a line with its name. Buttons can't sit inside
 * the link: the tags and copy buttons go next to it, in the box this component is.
 */
@Component({
    selector: 'sfe-folder-item',
    imports: [IconComponent, RouterLink, FolderCoverComponent, CopyPathButtonComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'folder-item', '[class.copied]': 'copied()', '[class.unindexed]': 'unindexed()', '[attr.data-path]': 'item().path' },
    template: `
        <a class="folder item kind-directory" [class.card]="cards()" routerLink="/" [queryParams]="store.browseParams(item().path)" [title]="linkTitle()">
            @if (cards()) {
                <sfe-folder-cover [path]="item().path" />
                <span class="folder-caption"><svg sfeIcon="folder"></svg><span class="item-name">{{ item().name }}</span></span>
            } @else {
                <svg sfeIcon="folder"></svg><span class="item-name">{{ item().name }}</span>
            }
        </a>
        <span class="folder-actions">
            @if (canTag()) {
                <button type="button" class="icon-button small folder-tags-button" [attr.aria-label]="'Tags of the files in ' + item().name"
                        title="Add or remove tags on all the files of this folder, or of its sub-folders too (asks first)" (click)="openTags()">
                    <svg sfeIcon="tag"></svg>
                </button>
            }
            @if (copyPaths.enabled()) {
                <button [sfeCopyPath]="item().path" [name]="item().name"></button>
            }
        </span>
    `,
})
export class FolderItemComponent {
    readonly item = input.required<LibraryItem>();
    /** Folders are cards with pictures (`config.folderPictures`), except in the list view. */
    readonly cards = input(false);
    private readonly config = inject(ConfigService);
    /** Tagging a whole folder changes tags: hidden without the right. */
    protected readonly canTag = computed(() => this.config.rights().has(Rights.assignTags));
    protected readonly store = inject(ListingStore);
    protected readonly copyPaths = inject(CopyPathService);
    protected readonly copied = computed(() => this.copyPaths.copied().has(this.item().path));
    protected readonly unindexed = computed(() => this.item().indexed === false);
    protected readonly linkTitle = computed(() => this.item().name + (this.unindexed() ? ` ${NOT_INDEXED}` : ''));
    private readonly dialogs = inject(Dialogs);

    /** The tags of the files of this folder (the dialog offers its sub-folders too). */
    protected openTags(): void {
        this.dialogs.scopeTags()?.open({ path: this.item().path, recursive: false, query: null });
    }
}
