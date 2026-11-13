import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, input, output, signal } from '@angular/core';
import { ToastService } from '../../core/toast.service';
import { BusyDirective, whileBusy } from '../../shared/busy.directive';
import { IconComponent } from '../../shared/icon.component';
import {
    FONT_SIZES,
    LINE_HEIGHTS,
    PADDINGS,
    READER_FONTS,
    READER_THEMES,
    ReaderTheme,
    ReadingProfile,
    WIDTHS,
} from './reading';
import { ReadingService } from './reading.service';

/**
 * The "Aa" panel of a text view: font, size, line height, width, margins, wrapping, line numbers and the reader's
 * colours, for the profile of the file shown (code or prose); applied as they change, kept by this browser; **Save as
 * my default** makes them the account's.
 * A popover under the button; a sheet at the bottom on phones.
 */
@Component({
    selector: 'sfe-reading-panel',
    imports: [BusyDirective, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'reading-panel',
        role: 'dialog',
        'aria-label': 'Reading settings',
        '(keydown.escape)': 'onEscape($event)',
        '(document:pointerdown)': 'onDocumentPointerDown($event)',
    },
    templateUrl: './reading-panel.component.html',
})
export class ReadingPanelComponent {
    readonly profile = input.required<ReadingProfile>();
    /** Esc, a click outside, or its close button. */
    readonly closed = output<void>();

    protected readonly reading = inject(ReadingService);
    private readonly element: HTMLElement = inject(ElementRef).nativeElement;
    private readonly toasts = inject(ToastService);
    protected readonly saving = signal(false);

    protected readonly fonts = READER_FONTS;
    protected readonly sizes = FONT_SIZES;
    protected readonly lineHeights = LINE_HEIGHTS;
    protected readonly widths = WIDTHS;
    protected readonly paddings = PADDINGS;
    protected readonly themes = READER_THEMES;
    protected readonly own = computed(() => this.reading.preferences()[this.profile()]);

    protected set<K extends 'font' | 'lineHeight' | 'width' | 'padding' | 'wrap' | 'lineNumbers'>(
        key: K,
        value: unknown,
    ): void {
        this.reading.update(this.profile(), { [key]: value });
    }

    protected async saveDefaults(): Promise<void> {
        try {
            await whileBusy(this.saving, () => this.reading.saveAsDefaults());
            this.toasts.show('Saved as your default: every browser you sign in from starts from these settings.');
        } catch (error) {
            this.toasts.error(`Could not save them: ${(error as Error).message}`);
        }
    }

    protected setTheme(theme: ReaderTheme): void {
        this.reading.setTheme(theme);
    }

    protected onEscape(event: Event): void {
        event.preventDefault();
        this.closed.emit();
    }

    /** A press outside closes it (the "Aa" button toggles it itself). */
    protected onDocumentPointerDown(event: Event): void {
        const target = event.target as Element | null;
        if (target && !this.element.contains(target) && !target.closest?.('[data-reading-toggle]')) this.closed.emit();
    }
}
