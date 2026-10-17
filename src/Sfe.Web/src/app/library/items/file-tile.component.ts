import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, Directive, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { CopyPathService } from '../../core/copy-path.service';
import { formatDate, formatSize, joinParts, kindIcon } from '../../core/format';
import { LibraryItem } from '../../core/models';
import { ItemMenu, ItemMenuButtonComponent } from '../../files/item-menu';
import { HighlightComponent } from '../../shared/highlight.component';
import { IconComponent } from '../../shared/icon.component';
import { TrickplayHoverService } from '../../trickplay/trickplay-hover.service';
import { TrickplayMarkComponent } from '../../trickplay/trickplay-mark.component';
import { AnimatedHoverService } from './animated-hover.service';
import { ListingStore } from '../listing.store';
import { ItemActions } from './item-actions.service';
import { CopyPathButtonComponent, ItemFolderComponent, ItemTagsComponent, NOT_INDEXED, SelectBoxComponent, TagButtonComponent } from './item-parts';
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
    protected readonly copyPaths = inject(CopyPathService);
    protected readonly config = inject(ConfigService);
    protected readonly menu = inject(ItemMenu);
    private readonly previews = inject(PreviewsService);

    protected readonly url = computed(() => this.config.links.file(this.item().path));
    protected readonly icon = computed(() => kindIcon[this.item().kind]);
    protected readonly sources = computed(() => this.previews.sources(this.item(),
        this.store.view() === 'single' && Boolean(this.config.config()?.singleViewOriginals)), { equal: sameStrings });
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
    protected readonly linkTitle = computed(() => this.item().name + (this.unindexed() ? ` ${NOT_INDEXED}` : ''));

    /** The box of the preview, where the hover previews play. */
    protected abstract previewContainer(): HTMLElement | undefined;

    constructor() {
        const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
        const hover = inject(TrickplayHoverService);
        const animated = inject(AnimatedHoverService);
        const destroyRef = inject(DestroyRef);
        afterNextRender(() => {
            if (this.isVideo())
                hover.attach(host, () => this.previewContainer()!, () => this.item().path, destroyRef);
            else if (this.item().kind === 'image')
                animated.attach(host, () => this.previewContainer()!, () => this.item(),
                    () => this.hasPreview() && this.sources()[0] !== this.url(), destroyRef);
        });
    }
}

function sameStrings(a: string[], b: string[]): boolean {
    return a.length === b.length && a.every((value, i) => value === b[i]);
}

/** A file in the grid and one-per-row views: its preview (with its HR/LR mark, a video's duration), name, folder (search results), resolution, size, date and tags. */
@Component({
    selector: 'article[sfeFileTile]',
    imports: [IconComponent, HighlightComponent, PreviewImageDirective, TrickplayMarkComponent, ItemTagsComponent, SelectBoxComponent,
        TagButtonComponent, CopyPathButtonComponent, ItemFolderComponent, ItemMenuButtonComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        '[class]': '"tile item kind-" + item().kind',
        '[class.no-preview]': '!hasPreview()',
        '[class.selected]': 'selected()',
        '[class.copied]': 'copied()',
        '[class.revealed]': 'revealed()',
        '[class.unindexed]': 'unindexed()',
        '[attr.data-path]': 'item().path',
        '[style.--ratio]': 'ratio()',
        '(contextmenu)': 'menu.fromEvent($event, item())',
    },
    template: `
        <a class="item-link" [href]="url()" target="_blank" rel="noopener" [title]="linkTitle()" (click)="actions.openFile($event, item())">
            <div #preview class="preview">
                <div class="placeholder"><svg [sfeIcon]="icon()"></svg><span class="ext">{{ item().extension || 'file' }}</span></div>
                @if (hasPreview()) {
                    <img [sfePreview]="sources()" (shape)="learnShape($event)" (failed)="previewFailed.set(true)">
                }
                <span class="preview-marks">
                    @if (resolutionMark(); as mark) {
                        <span class="resolution-mark" [class.high]="mark.high" [title]="mark.title">{{ mark.label }}</span>
                    }
                    @if (item().kind === 'video' || item().kind === 'audio') {
                        <span class="badge"><svg sfeIcon="play"></svg>{{ item().extension || '' }}</span>
                    }
                </span>
                @if (isVideo() || durationBadge()) {
                    <span class="preview-marks start">
                        @if (durationBadge()) {
                            <span class="badge duration" [title]="'Duration: ' + duration()">{{ duration() }}</span>
                        }
                        @if (isVideo()) {
                            <sfe-trickplay-mark [path]="item().path" />
                        }
                    </span>
                }
            </div>
        </a>
        <label sfeSelectBox [item]="item()"></label>
        <button [sfeTagButton]="item()"></button>
        <button [sfeItemMenu]="item()"></button>
        @if (copyPaths.enabled()) {
            <button [sfeCopyPath]="item().path" [name]="item().name"></button>
        }
        <div class="info">
            <a class="item-link" [href]="url()" target="_blank" rel="noopener" [title]="linkTitle()" (click)="actions.openFile($event, item())">
                <span class="item-name"><sfe-highlight [text]="item().name" [pattern]="pattern()" /></span>
            </a>
            <sfe-item-folder [item]="item()" [pattern]="pattern()" />
            <span class="meta">{{ meta() }}</span>
            <sfe-item-tags [item]="item()" />
        </div>
    `,
})
export class FileTileComponent extends FileItemBase {
    private readonly previewBox = viewChild<ElementRef<HTMLElement>>('preview');
    private readonly shapes = inject(PreviewsService);
    private readonly learned = signal<number | null>(null);
    /**
     * The preview's shape (width / height), which the tile takes: known from an earlier render (other layout, back
     * navigation), or once loaded.
     */
    protected readonly ratio = computed(() => {
        const ratio = this.hasPreview() ? this.learned() ?? this.shapes.knownRatio(this.item().path) : null;
        return ratio ? ratio.toFixed(4) : null;
    });
    protected readonly duration = computed(() => this.config.duration(this.item()));
    /**
     * The duration goes on the preview: over a video's thumbnail, and over an audio file's placeholder (or cover) since
     * audio rarely has a picture. A video whose preview can't be shown keeps it in its details.
     */
    protected readonly durationBadge = computed(() =>
        Boolean(this.duration()) && (this.item().kind === 'audio' || this.isVideo() && this.hasPreview()));
    protected readonly meta = computed(() => joinParts([this.config.resolution(this.item()),
        this.durationBadge() ? '' : this.duration(), formatSize(this.item().size), formatDate(this.item().modified)]));

    protected previewContainer() {
        return this.previewBox()?.nativeElement;
    }

    protected learnShape(ratio: number): void {
        this.shapes.learn(this.item().path, ratio);
        this.learned.set(ratio);
        this.shapes.scheduleGuess(() => this.store.items());
    }
}

/** A file in the list view: check box, thumbnail, name, folder and tags, type, size (and resolution, duration), date and buttons. */
@Component({
    selector: 'div[sfeFileRow]',
    imports: [IconComponent, HighlightComponent, PreviewImageDirective, TrickplayMarkComponent, ItemTagsComponent, SelectBoxComponent,
        TagButtonComponent, CopyPathButtonComponent, ItemFolderComponent, ItemMenuButtonComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        '[class]': '"row item kind-" + item().kind',
        '[class.selected]': 'selected()',
        '[class.copied]': 'copied()',
        '[class.revealed]': 'revealed()',
        '[class.unindexed]': 'unindexed()',
        '[attr.data-path]': 'item().path',
        '(contextmenu)': 'menu.fromEvent($event, item())',
    },
    template: `
        <label sfeSelectBox [item]="item()"></label>
        <a class="item-link" [href]="url()" target="_blank" rel="noopener" [title]="linkTitle()" (click)="actions.openFile($event, item())">
            <span #thumb class="thumb">
                <svg [sfeIcon]="icon()"></svg>
                @if (hasPreview()) {
                    <img [sfePreview]="sources()" (failed)="previewFailed.set(true)">
                }
                @if (resolutionMark(); as mark) {
                    <span class="resolution-mark" [class.high]="mark.high" [title]="mark.title">{{ mark.label }}</span>
                }
                @if (isVideo()) {
                    <sfe-trickplay-mark [path]="item().path" />
                }
            </span>
        </a>
        <div class="row-main">
            <a class="item-link" [href]="url()" target="_blank" rel="noopener" [title]="linkTitle()" (click)="actions.openFile($event, item())">
                <span class="item-name"><sfe-highlight [text]="item().name" [pattern]="pattern()" /></span>
            </a>
            <sfe-item-folder [item]="item()" [pattern]="pattern()" />
            <sfe-item-tags [item]="item()" />
        </div>
        <span class="type" [title]="item().extension ? '.' + item().extension + ' file' : 'No extension'">{{ item().extension || '—' }}</span>
        <span class="size">{{ size() }}@if (resolution()) {<span class="resolution">{{ resolution() }}</span>}@if (duration()) {<span class="duration">{{ duration() }}</span>}</span>
        <span class="date">{{ date() }}</span>
        <span class="row-actions">
            @if (copyPaths.enabled()) {
                <button [sfeCopyPath]="item().path" [name]="item().name"></button>
            }
            <button [sfeTagButton]="item()"></button>
            <button [sfeItemMenu]="item()"></button>
        </span>
    `,
})
export class FileRowComponent extends FileItemBase {
    private readonly thumb = viewChild<ElementRef<HTMLElement>>('thumb');
    protected readonly size = computed(() => formatSize(this.item().size));
    protected readonly resolution = computed(() => this.config.resolution(this.item()));
    protected readonly duration = computed(() => this.config.duration(this.item()));
    protected readonly date = computed(() => formatDate(this.item().modified));

    protected previewContainer() {
        return this.thumb()?.nativeElement;
    }
}
