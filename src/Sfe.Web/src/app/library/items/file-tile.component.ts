import {
    afterNextRender,
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    Directive,
    ElementRef,
    inject,
    input,
    signal,
    viewChild,
} from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { CopyPathService } from '../../core/copy-path.service';
import { formatDate, formatSize, joinParts, kindIcon } from '../../core/format';
import { LibraryItem } from '../../core/models';
import { FileTransfers } from '../../files/file-transfers';
import { BEING_WRITTEN_TITLE, isPartial, shownName } from '../../files/partial-files';
import { ItemMenu, ItemMenuButtonComponent } from '../../files/item-menu';
import { useContextMenu } from '../../menus/context-menu';
import { HighlightComponent } from '../../shared/highlight.component';
import { IconComponent } from '../../shared/icon.component';
import { TrickplayHoverService } from '../../trickplay/trickplay-hover.service';
import { TrickplayMarkComponent } from '../../trickplay/trickplay-mark.component';
import { AnimatedHoverService } from './animated-hover.service';
import { ListingStore } from '../listing.store';
import { ItemActions } from './item-actions.service';
import {
    ItemFolderComponent,
    ItemTagsComponent,
    LockMarkComponent,
    NOT_INDEXED,
    PartialMarkComponent,
    SelectBoxComponent,
    TagButtonComponent,
} from './item-parts';
import { PreviewImageDirective } from './preview-image.directive';
import { PreviewsService } from './previews.service';

/**
 * What a listed file shows, shared by its tile and its row: the preview, which only exists for images and videos
 * and goes away when none of its sources can be shown, the trickplay hover on videos, the animated image hover on
 * images shown through their thumbnail, the selection.
 */
@Directive()
abstract class FileItemBase {
    readonly item = input.required<LibraryItem>();
    readonly pattern = input<RegExp | null>(null);

    protected readonly store = inject(ListingStore);
    protected readonly actions = inject(ItemActions);
    private readonly copyPaths = inject(CopyPathService);
    protected readonly config = inject(ConfigService);
    private readonly menu = inject(ItemMenu);
    private readonly previews = inject(PreviewsService);
    private readonly transfers = inject(FileTransfers);
    /** Cut (Ctrl+X): faded until pasted. */
    protected readonly cut = computed(() => this.transfers.cutPaths().has(this.item().path));

    /** Still being written (`isPartial`): marked, no link, no preview. */
    protected readonly partial = computed(() => isPartial(this.item()));
    /** Its name, or the one it takes once written. */
    protected readonly name = computed(() => shownName(this.item()));
    /** Opened by a modified click (new tab...): the file, or its viewer's page for formats that have one. */
    protected readonly url = computed(() => (this.partial() ? null : this.actions.fileUrl(this.item())));
    protected readonly icon = computed(() => kindIcon[this.item().kind]);
    protected readonly sources = computed(
        () =>
            this.previews
                .sources(
                    this.item(),
                    this.store.view() === 'single' && Boolean(this.config.config()?.singleViewOriginals),
                )
                .filter(() => !this.partial()),
        { equal: sameStrings },
    );
    protected readonly previewFailed = signal(false);
    protected readonly hasPreview = computed(() => this.sources().length > 0 && !this.previewFailed());
    protected readonly selected = computed(() => this.store.selection().has(this.item().path));
    protected readonly copied = computed(() => this.copyPaths.copied().has(this.item().path));
    /** Just shown by a quick link (`ListingStore.reveal`). */
    protected readonly revealed = computed(() => this.store.revealed() === this.item().path);
    protected readonly isVideo = computed(() => this.item().kind === 'video');
    protected readonly resolutionMark = computed(() => this.config.resolutionMark(this.item()));
    protected readonly unindexed = computed(() => this.item().indexed === false);
    /** The links' hint: the name, and whether the index has the file yet. */
    protected readonly linkTitle = computed(() =>
        [this.name(), this.partial() ? BEING_WRITTEN_TITLE : this.unindexed() ? NOT_INDEXED : null]
            .filter(Boolean)
            .join('\n'),
    );
    /** What a file without an extension shows on its placeholder. */
    protected readonly noExtension = $localize`:placeholder of a file without an extension:file`;

    /** The box of the preview, where the hover previews play. */
    protected abstract previewContainer(): HTMLElement | undefined;

    constructor() {
        useContextMenu(() => this.menu.target(this.item()));
        const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
        const hover = inject(TrickplayHoverService);
        const animated = inject(AnimatedHoverService);
        const destroyRef = inject(DestroyRef);
        afterNextRender(() => {
            if (this.isVideo())
                hover.attach(
                    host,
                    () => this.previewContainer()!,
                    () => this.item().path,
                    destroyRef,
                );
            else if (this.item().kind === 'image')
                animated.attach(
                    host,
                    () => this.previewContainer()!,
                    () => this.item(),
                    () => this.hasPreview() && this.sources()[0] !== this.url(),
                    destroyRef,
                );
        });
    }
}

function sameStrings(a: string[], b: string[]): boolean {
    return a.length === b.length && a.every((value, i) => value === b[i]);
}

/**
 * A file in the grid and one-per-row views: its preview (with its HR/LR mark, a video's duration), name, folder (search
 * results), resolution, size, date and tags.
 */
@Component({
    selector: 'article[sfeFileTile]',
    imports: [
        IconComponent,
        HighlightComponent,
        PreviewImageDirective,
        TrickplayMarkComponent,
        ItemTagsComponent,
        SelectBoxComponent,
        TagButtonComponent,
        ItemFolderComponent,
        ItemMenuButtonComponent,
        PartialMarkComponent,
        LockMarkComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        '[class]': '"tile item kind-" + item().kind',
        '[class.no-preview]': '!hasPreview()',
        '[class.selected]': 'selected()',
        '[class.copied]': 'copied()',
        '[class.cut]': 'cut()',
        '[class.revealed]': 'revealed()',
        '[class.unindexed]': 'unindexed()',
        '[class.partial]': 'partial()',
        '[attr.data-path]': 'item().path',
        '[style.--ratio]': 'ratio()',
    },
    templateUrl: './file-tile.component.html',
})
export class FileTileComponent extends FileItemBase {
    private readonly previewBox = viewChild<ElementRef<HTMLElement>>('preview');
    private readonly shapes = inject(PreviewsService);
    private readonly learned = signal<number | null>(null);
    /**
     * The preview's shape (width / height), which the tile takes: once loaded, else known before (an earlier render,
     * the index: `PreviewsService.shapeOf`); without one, the layout's guess (files.css).
     */
    protected readonly ratio = computed(() => {
        const ratio = this.hasPreview() ? (this.learned() ?? this.shapes.shapeOf(this.item())) : null;
        return ratio ? ratio.toFixed(4) : null;
    });
    protected readonly duration = computed(() => this.config.duration(this.item()));
    protected readonly durationTitle = computed(() => $localize`It lasts ${this.duration()}:duration:.`);
    /**
     * The duration goes on the preview: over a video's thumbnail, and over an audio file's placeholder (or cover) since
     * audio rarely has a picture. A video whose preview can't be shown keeps it in its details.
     */
    protected readonly durationBadge = computed(
        () => Boolean(this.duration()) && (this.item().kind === 'audio' || (this.isVideo() && this.hasPreview())),
    );
    protected readonly meta = computed(() =>
        joinParts([
            this.config.resolution(this.item()),
            this.durationBadge() ? '' : this.duration(),
            formatSize(this.item().size),
            formatDate(this.item().modified),
        ]),
    );

    protected previewContainer() {
        return this.previewBox()?.nativeElement;
    }

    protected learnShape(ratio: number): void {
        this.shapes.learn(this.item().path, ratio);
        this.learned.set(ratio);
        this.shapes.scheduleGuess(() => this.store.items());
    }
}

/**
 * A file in the list view: check box, thumbnail, name, folder and tags, type, size (and resolution, duration), date and
 * buttons. A narrow list (a phone, landscape with the sidebar) gives the name the whole line and puts type, size and
 * resolution under it (`.row-meta`, shown by a container query in files.css) instead of their columns.
 */
@Component({
    selector: 'div[sfeFileRow]',
    imports: [
        IconComponent,
        HighlightComponent,
        PreviewImageDirective,
        TrickplayMarkComponent,
        ItemTagsComponent,
        SelectBoxComponent,
        TagButtonComponent,
        ItemFolderComponent,
        ItemMenuButtonComponent,
        PartialMarkComponent,
        LockMarkComponent,
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        '[class]': '"row item kind-" + item().kind',
        '[class.selected]': 'selected()',
        '[class.copied]': 'copied()',
        '[class.cut]': 'cut()',
        '[class.revealed]': 'revealed()',
        '[class.unindexed]': 'unindexed()',
        '[class.partial]': 'partial()',
        '[attr.data-path]': 'item().path',
    },
    templateUrl: './file-row.component.html',
})
export class FileRowComponent extends FileItemBase {
    private readonly thumb = viewChild<ElementRef<HTMLElement>>('thumb');
    protected readonly size = computed(() => formatSize(this.item().size));
    protected readonly resolution = computed(() => this.config.resolution(this.item()));
    protected readonly duration = computed(() => this.config.duration(this.item()));
    protected readonly date = computed(() => formatDate(this.item().modified));
    protected readonly typeTitle = computed(() => {
        const extension = this.item().extension;
        return extension ? $localize`A “.${extension}:extension:” file.` : $localize`This file has no extension.`;
    });
    /** The narrow list's line under the name: size, resolution, duration (after the type, styled apart). */
    protected readonly meta = computed(() => joinParts([this.size(), this.resolution(), this.duration()]));

    protected previewContainer() {
        return this.thumb()?.nativeElement;
    }
}
