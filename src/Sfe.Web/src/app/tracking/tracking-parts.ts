import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { ConfigService } from '../core/config.service';
import { CopyPathService } from '../core/copy-path.service';
import { formatDate, kindIcon } from '../core/format';
import { LibraryItem } from '../core/models';
import { parentPath } from '../core/paths';
import { Rights } from '../core/rights';
import { ItemActions } from '../library/items/item-actions.service';
import { ListingStore } from '../library/listing.store';
import { MenuEntry, useContextMenu } from '../menus/context-menu';
import { IconComponent } from '../shared/icon.component';
import { TagChipComponent } from '../tags/tag-chip.component';

/**
 * A file's line: preview, name (opens the file), folder (`openFolder`), tags, date, then the projected actions. Its
 * menu (a right click): the file in a new tab, its folder (here or in a new tab), download, copy its path.
 */
@Component({
    selector: 'li[sfeTrackingFile]',
    imports: [IconComponent, TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'tracking-row' },
    template: `
        @if (thumbnail() && !failed()) {
            <img class="tracking-thumb" [src]="thumbnail()" alt="" loading="lazy" decoding="async" (error)="failed.set(true)">
        } @else {
            <span class="tracking-thumb"><svg [sfeIcon]="icon()"></svg></span>
        }
        <span class="tracking-file">
            <a class="tracking-name" [href]="urls()!.file(file().path)" target="_blank" rel="noopener" [title]="'Open ' + file().path">{{ file().name }}</a>
            <button type="button" class="tracking-folder" title="Show this folder" (click)="openFolder.emit(file().directory ?? '')">
                <svg sfeIcon="folder"></svg>{{ file().directory || config.config()?.rootName }}
            </button>
            @if (file().tags?.length) {
                <span class="tracking-tags">
                    @for (tag of file().tags; track tag.id) {
                        <sfe-tag-chip [tag]="tag" />
                    }
                </span>
            }
        </span>
        <span class="tracking-detail">{{ date() }}</span>
        <ng-content />
    `,
})
export class TrackingFileComponent {
    readonly file = input.required<LibraryItem>({ alias: 'sfeTrackingFile' });
    readonly openFolder = output<string>();

    protected readonly config = inject(ConfigService);
    protected readonly urls = this.config.urls;
    protected readonly failed = signal(false);
    protected readonly icon = computed(() => kindIcon[this.file().kind] ?? 'file');
    protected readonly date = computed(() => formatDate(this.file().modified));
    private readonly actions = inject(ItemActions);
    private readonly store = inject(ListingStore);
    private readonly copyPaths = inject(CopyPathService);

    constructor() {
        useContextMenu(() => {
            const file = this.file();
            return { kind: 'file', key: `tracking:${file.path}`, name: file.name, item: file, entries: () => this.menuEntries(file) };
        });
    }

    private menuEntries(file: LibraryItem): MenuEntry[] {
        const folder = file.directory ?? parentPath(file.path);
        const folderName = folder || this.config.config()?.rootName || '';
        const entries: MenuEntry[] = [
            { id: 'newTab', label: 'Open in new tab', icon: 'external', title: `Open ${file.name} in a tab of its own`, href: this.actions.fileUrl(file), newTab: true },
            { id: 'openFolder', label: 'Open containing folder', icon: 'folder', title: `List ${folderName}`, run: () => this.openFolder.emit(folder) },
            { id: 'folderNewTab', label: 'Open containing folder in new tab', icon: 'external', title: `List ${folderName} in a tab of its own`, href: this.store.pageUrl(this.store.browseParams(folder)), newTab: true },
        ];
        if (this.config.can(Rights.download))
            entries.push({ id: 'download', label: 'Download', icon: 'download', title: 'Save this file to this device', href: this.urls()!.download(file.path) });
        if (this.copyPaths.enabled())
            entries.push({ id: 'copyPath', label: 'Copy full path', icon: 'copy', title: 'Copy where it is on the server', run: () => void this.copyPaths.copy(file.path) });
        return entries;
    }

    protected readonly thumbnail = computed(() => {
        const file = this.file();
        return this.config.config()?.features.thumbnails && (file.kind === 'image' || file.kind === 'video') ? this.urls()!.thumbnail(file.path) : null;
    });
}

/** The summary box at the top of a panel: a state dot, a line of text, details below, then the projected actions. */
@Component({
    selector: 'sfe-status-box',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'settings-status', 'aria-live': 'polite', '[attr.data-state]': 'state()' },
    template: `
        <div class="settings-status-line"><span class="settings-status-dot"></span><span class="settings-status-text">{{ text() }}</span></div>
        <span class="field-hint">{{ detail() }}</span>
        <ng-content />
    `,
})
export class StatusBoxComponent {
    readonly state = input<string | null>(null);
    readonly text = input('…');
    readonly detail = input('');
}
