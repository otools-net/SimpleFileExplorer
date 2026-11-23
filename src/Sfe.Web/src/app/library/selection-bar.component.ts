import { ChangeDetectionStrategy, Component, computed, ElementRef, inject } from '@angular/core';
import { icu } from '../core/i18n/icu';
import { ItemCommands } from '../files/item-commands';
import { ItemCommandId, ItemMenuEntry, ItemScope } from '../files/item-scope';
import { MenuEntry } from '../menus/context-menu';
import { OverflowItemDirective, OverflowMoreComponent, OverflowRowDirective } from '../menus/overflow-row';
import { IconComponent } from '../shared/icon.component';
import { ListingStore } from './listing.store';

/**
 * The bar at the bottom while files or folders are selected: their count, select all, adding them to the quick links,
 * renaming the one selected, moving, copying, downloading and deleting them, the tags of the files selected.
 * One line: on a narrow screen the count shortens ("15 selected"), then the buttons leave from the right (Download
 * first, Quick links last) into its "⋯" menu; Clear, the count, Select all and Tags stay. The host is a full-width
 * strip at the bottom, the bar centred in it: the room the bar has is measured there (`overflowIn`).
 */
@Component({
    selector: 'sfe-selection-bar',
    imports: [IconComponent, OverflowRowDirective, OverflowItemDirective, OverflowMoreComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'selection-dock', role: 'region', '[attr.aria-label]': 'label', '[hidden]': '!count()' },
    templateUrl: './selection-bar.component.html',
})
export class SelectionBarComponent {
    protected readonly store = inject(ListingStore);
    private readonly commands = inject(ItemCommands);

    protected readonly count = computed(() => this.store.selection().size);
    private readonly selected = computed(() => this.store.selectedItems());
    protected readonly files = computed(() => this.selected().filter((item) => item.kind !== 'directory').length);
    private readonly folders = computed(() => this.selected().length - this.files());
    protected readonly loaded = computed(() => this.store.items().length);
    protected readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    protected readonly label = $localize`Selected items`;
    /** The count when the bar is narrow: "15 selected". */
    protected readonly shortCountText = computed(() =>
        icu($localize`{count, plural, other {# selected}}`, { count: this.count() }),
    );
    protected readonly countText = computed(() => {
        const [folders, files] = [this.folders(), this.files()];
        if (folders && files)
            return icu(
                $localize`{folders, plural, one {# folder} other {# folders}} and \
{files, plural, one {# file} other {# files}} selected`,
                { folders, files },
            );
        if (folders)
            return icu($localize`{folders, plural, one {# folder selected} other {# folders selected}}`, { folders });
        if (files) return icu($localize`{files, plural, one {# file selected} other {# files selected}}`, { files });
        return icu($localize`{count, plural, one {# item selected} other {# items selected}}`, { count: this.count() });
    });
    protected readonly allTitle = computed(() =>
        this.store.hasMore()
            ? icu(
                  $localize`{count, plural, \
one {Select the # item loaded so far (Ctrl+A). Scroll down to load more.} \
other {Select the # items loaded so far (Ctrl+A). Scroll down to load more.}}`,
                  { count: this.loaded() },
              )
            : $localize`Select every file and folder listed (Ctrl+A).`,
    );
    /** What the selection acts on (`ItemCommands`); null while nothing is selected. */
    private readonly scope = computed<ItemScope | null>(() => {
        const targets = this.selected();
        return targets.length ? { item: targets[0], targets } : null;
    });

    /**
     * The buttons, from the same commands as the menus: those the selection may use (no greyed out button: the bar
     * has little room); extracting is the dialog for one archive, each beside it for several; Tags only with files.
     */
    protected readonly actions = computed(() => ({
        quickLink: this.usable('quickLink'),
        rename: this.usable('rename'),
        delete: this.usable('delete'),
        moveTo: this.usable('moveTo'),
        copyTo: this.usable('copyTo'),
        extract: this.usable('extract') ?? this.usable('extractEach'),
        compress: this.usable('compress'),
        download: this.usable('download'),
        tags: this.files() ? this.usable('tags') : null,
    }));

    /** What the buttons that may leave the bar are in its "⋯" menu: the same entries, without the menus' groups. */
    protected readonly more = {
        quickLink: () => this.inMore('quickLink'),
        rename: () => this.inMore('rename'),
        delete: () => this.inMore('delete'),
        moveTo: () => this.inMore('moveTo'),
        copyTo: () => this.inMore('copyTo'),
        extract: () => this.inMore('extract'),
        compress: () => this.inMore('compress'),
        download: () => this.inMore('download'),
    };

    private usable(id: ItemCommandId): ItemMenuEntry | null {
        const scope = this.scope();
        const entry = scope && this.commands.entry(id, scope);
        return entry && !entry.disabled ? entry : null;
    }

    private inMore(action: keyof SelectionBarComponent['more']): MenuEntry {
        return { ...this.actions()[action]!, group: undefined };
    }
}
