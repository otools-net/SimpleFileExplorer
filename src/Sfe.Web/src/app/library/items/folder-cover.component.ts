import { ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, input, OnInit } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { IconComponent } from '../../shared/icon.component';
import { CoversService } from './covers.service';
import { PreviewImageDirective } from './preview-image.directive';
import { PreviewsService } from './previews.service';

/**
 * The picture area of a folder card: the folder icon until its pictures are known, and when it has none; otherwise
 * `config.folderPictures` cells in as square a grid as the count allows (4 → 2×2, 6 → 3×2, 9 → 3×3).
 */
@Component({
    selector: 'sfe-folder-cover',
    imports: [IconComponent, PreviewImageDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'folder-cover',
        '[class.has-media]': 'cells().length > 0',
        '[style.--cover-columns]': 'columns()',
        '[style.--cover-rows]': 'rows()',
    },
    template: `
        <svg sfeIcon="folder"></svg>
        @for (cell of cells(); track $index) {
            <span class="cell">
                @if (cell.length) {
                    <img [sfePreview]="cell">
                }
            </span>
        }
    `,
})
export class FolderCoverComponent implements OnInit {
    readonly path = input.required<string>();

    private readonly covers = inject(CoversService);
    private readonly previews = inject(PreviewsService);
    private readonly config = inject(ConfigService);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

    private readonly count = computed(() => this.config.config()?.folderPictures ?? 0);
    protected readonly columns = computed(() => Math.ceil(Math.sqrt(this.count())));
    protected readonly rows = computed(() => Math.ceil(this.count() / this.columns()));
    /** The preview sources of each cell; empty when the folder has no pictures (yet). */
    protected readonly cells = computed(() => {
        const items = this.covers.covers().get(this.path()) ?? [];
        return items.length ? Array.from({ length: this.count() }, (_, i) => items[i] ? this.previews.sources(items[i]) : []) : [];
    });

    constructor() {
        inject(DestroyRef).onDestroy(() => this.covers.unwatch(this.host));
    }

    ngOnInit(): void {
        this.covers.watch(this.host, this.path());
    }
}
