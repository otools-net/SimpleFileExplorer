import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { InheritedDefaultTag, TagSummary } from '../core/models';
import { IconComponent } from '../shared/icon.component';
import { TagChipComponent } from './tag-chip.component';

let nextId = 0;

/** A parent folder named in the panel: its last part, or "the top folder". */
function folderLabel(path: string): string {
    return path ? `“${path.slice(path.lastIndexOf('/') + 1)}”` : 'the top folder';
}

/**
 * A tag icon telling that a folder has default tags (agents/docs/features/default-tags.md), whatever their number: its
 * panel, opened by a click or a tap (not on hover: tried, too much), lists them in full, the folder's own then those
 * it gets from its parents. On a phone it is a sheet at the bottom of the screen over a backdrop
 * (styles/phone-sheets.css). Esc, a click elsewhere, a scroll or the icon again closes it. With `editable`, its
 * **Change…** button emits `edit` (the folder's tags dialog).
 */
@Component({
    selector: 'sfe-default-tags-badge',
    imports: [IconComponent, TagChipComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'default-tags-badge',
        '(keydown.escape)': 'close(true)',
    },
    template: `
        <button #button type="button" class="default-tags-button" [class.open]="open()" [attr.aria-expanded]="open()" [attr.aria-controls]="id"
                [attr.aria-label]="label()" [title]="open() ? '' : label()" (click)="toggle()">
            <svg sfeIcon="tag-auto"></svg>
            @if (count() > 1) {
                <span class="default-tags-count">{{ count() }}</span>
            }
        </button>
        @if (open()) {
            <!-- The layer is the popover (the top layer); it lets the pointer through but to its panel, and on phones to the backdrop. -->
            <div #panel class="default-tags-layer" popover="manual">
            <div class="sheet-backdrop" aria-hidden="true" (click)="close(false)"></div>
            <div class="default-tags-popover phone-sheet" role="dialog" [id]="id" [attr.aria-label]="'Default tags of ' + folder()"
                 [style.--pop-top.px]="position().top" [style.--pop-bottom.px]="position().bottom" [style.--pop-left.px]="position().left">
                <div class="default-tags-popover-head">
                    <strong>Default tags</strong>
                    <button type="button" class="icon-button small ghost default-tags-close" aria-label="Close" title="Close (Esc)" (click)="close(true)">
                        <svg sfeIcon="close"></svg>
                    </button>
                </div>
                <p class="field-hint">Files put into {{ folder() }} or its sub-folders get them.</p>
                @if (own().length) {
                    <div class="tag-list">
                        @for (tag of own(); track tag.id) {
                            <sfe-tag-chip [tag]="tag" />
                        }
                    </div>
                }
                @for (group of inheritedGroups(); track group.path) {
                    <p class="default-tags-from">From {{ group.label }}</p>
                    <div class="tag-list">
                        @for (tag of group.tags; track tag.id) {
                            <sfe-tag-chip [tag]="tag" />
                        }
                    </div>
                }
                @if (editable()) {
                    <button type="button" class="button small default-tags-edit" title="Add or remove default tags of this folder" (click)="change()">Change…</button>
                }
            </div>
            </div>
        }
    `,
})
export class DefaultTagsBadgeComponent {
    /** The folder's own default tags. */
    readonly own = input<readonly TagSummary[]>([]);
    /** Those it gets from its parents. */
    readonly inherited = input<readonly InheritedDefaultTag[]>([]);
    /** The folder, as the panel names it: “Family”. */
    readonly folder = input.required<string>();
    readonly editable = input(false);
    readonly edit = output<void>();

    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    private readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('button');
    private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
    protected readonly id = `default-tags-${++nextId}`;
    protected readonly open = signal(false);
    protected readonly position = signal<{ top: number | null; bottom: number | null; left: number }>({ top: 0, bottom: null, left: 0 });

    protected readonly count = computed(() => this.own().length + this.inherited().length);
    protected readonly label = computed(() => {
        const names = [...this.own(), ...this.inherited().map(entry => entry.tag)].map(tag => tag.displayName || tag.name);
        return `Default tags: ${names.join(', ')}`;
    });
    protected readonly inheritedGroups = computed(() => {
        const groups = new Map<string, TagSummary[]>();
        for (const entry of this.inherited())
            groups.set(entry.path, [...groups.get(entry.path) ?? [], entry.tag]);
        return [...groups].map(([path, tags]) => ({ path, label: folderLabel(path), tags }));
    });

    constructor() {
        // In the top layer: a transformed or clipping ancestor (a card lifting on hover) would otherwise hold the fixed
        // panel. Browsers without the popover API (and jsdom) keep it in place, where it mostly works too.
        let shown: HTMLElement | null = null;
        effect(() => {
            const panel = this.panel()?.nativeElement ?? null;
            if (panel && panel !== shown && typeof panel.showPopover === 'function')
                panel.showPopover();
            shown = panel;
        });
        const outside = (event: Event) => {
            if (this.open() && !this.host.contains(event.target as Node))
                this.close(false);
        };
        // A page scrolling under the fixed panel would leave it pointing nowhere; its own content may scroll.
        const scrolled = (event: Event) => {
            if (this.open() && !this.host.contains(event.target as Node))
                this.close(false);
        };
        document.addEventListener('pointerdown', outside, true);
        document.addEventListener('scroll', scrolled, true);
        window.addEventListener('resize', scrolled);
        inject(DestroyRef).onDestroy(() => {
            document.removeEventListener('pointerdown', outside, true);
            document.removeEventListener('scroll', scrolled, true);
            window.removeEventListener('resize', scrolled);
        });
    }

    protected toggle(): void {
        if (this.open())
            this.close(false);
        else
            this.show();
    }

    /** Closes the panel; `focus`: back on the icon (Esc, ×). */
    close(focus: boolean): void {
        if (!this.open())
            return;
        this.open.set(false);
        if (focus)
            this.button().nativeElement.focus();
    }

    protected change(): void {
        this.close(false);
        this.edit.emit();
    }

    /** Next to the icon, kept inside the window (above it when there is no room below); phones ignore it (CSS). */
    private show(): void {
        const rect = this.button().nativeElement.getBoundingClientRect();
        const width = Math.min(320, window.innerWidth - 16);
        // From the icon's left edge rightwards, else (near the window's right side) ending at its right edge.
        const left = Math.max(8, rect.left + width <= window.innerWidth - 8 ? rect.left : Math.min(rect.right - width, window.innerWidth - width - 8));
        // Room for a few lines of tags below, else above when there is more room there.
        const above = window.innerHeight - rect.bottom < 220 && rect.top > window.innerHeight - rect.bottom;
        this.position.set(above
            ? { top: null, bottom: window.innerHeight - rect.top + 6, left }
            : { top: rect.bottom + 6, bottom: null, left });
        this.open.set(true);
    }
}
