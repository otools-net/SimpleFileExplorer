import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, input, output, signal } from '@angular/core';
import { ToastService } from '../../core/toast.service';
import { BusyDirective, whileBusy } from '../../shared/busy.directive';
import { IconComponent } from '../../shared/icon.component';
import { FONT_SIZES, LINE_HEIGHTS, PADDINGS, READER_FONTS, READER_THEMES, ReaderTheme, ReadingProfile, WIDTHS } from './reading';
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
    template: `
        <header class="reading-panel-head">
            <strong>{{ profile() === 'prose' ? 'Reading text' : 'Reading code' }}</strong>
            <button type="button" class="icon-button ghost small" title="Close (Esc)" aria-label="Close" (click)="closed.emit()"><svg sfeIcon="close"></svg></button>
        </header>
        <label class="reading-field">
            <span>Font</span>
            <select (change)="set('font', $any($event.target).value)">
                @for (font of fonts; track font.id) {
                    <option [value]="font.id" [selected]="font.id === own().font">{{ font.label }}</option>
                }
            </select>
        </label>
        @if (own().font === 'custom') {
            <label class="reading-field">
                <span>Font name</span>
                <input type="text" maxlength="100" placeholder="e.g. Fira Code" [value]="reading.preferences().customFont"
                       title="The name of a font installed on this device" (input)="reading.setCustomFont($any($event.target).value)">
            </label>
        }
        <div class="reading-field">
            <span>Size</span>
            <div class="reading-size">
                <button type="button" class="icon-button small" title="Smaller (Ctrl+−)" aria-label="Smaller" [disabled]="own().size <= sizes.min" (click)="reading.changeSize(profile(), -1)">A−</button>
                <output aria-live="polite">{{ own().size }} px</output>
                <button type="button" class="icon-button small" title="Larger (Ctrl+=)" aria-label="Larger" [disabled]="own().size >= sizes.max" (click)="reading.changeSize(profile(), 1)">A+</button>
            </div>
        </div>
        <label class="reading-field">
            <span>Line height</span>
            <select (change)="set('lineHeight', +$any($event.target).value)">
                @for (value of lineHeights; track value) {
                    <option [value]="value" [selected]="value === own().lineHeight">{{ value }}</option>
                }
            </select>
        </label>
        <label class="reading-field">
            <span>Width</span>
            <select title="The longest line, when long lines wrap" (change)="set('width', +$any($event.target).value)">
                @for (value of widths; track value) {
                    <option [value]="value" [selected]="value === own().width">{{ value ? value + ' characters' : 'Whole width' }}</option>
                }
            </select>
        </label>
        <label class="reading-field">
            <span>Margins</span>
            <select (change)="set('padding', +$any($event.target).value)">
                @for (value of paddings; track value) {
                    <option [value]="value" [selected]="value === own().padding">{{ value ? value + ' px' : 'None' }}</option>
                }
            </select>
        </label>
        <label class="reading-check"><input type="checkbox" [checked]="own().wrap" (change)="set('wrap', $any($event.target).checked)"> Wrap long lines</label>
        <label class="reading-check"><input type="checkbox" [checked]="own().lineNumbers" (change)="set('lineNumbers', $any($event.target).checked)"> Line numbers</label>
        <div class="reading-field">
            <span>Colours</span>
            <div class="reading-themes" role="radiogroup" aria-label="Colours">
                @for (theme of themes; track theme.id) {
                    <button type="button" role="radio" class="reading-theme" [attr.data-reader-theme]="theme.id" [class.active]="theme.id === reading.preferences().theme"
                            [attr.aria-checked]="theme.id === reading.preferences().theme" [title]="theme.title" (click)="setTheme(theme.id)">{{ theme.label }}</button>
                }
            </div>
        </div>
        <footer class="reading-panel-foot">
            <button type="button" class="button small" title="These settings and the colours back to your defaults" (click)="reading.reset(profile())">Reset</button>
            @if (reading.canSaveDefaults()) {
                <button type="button" class="button small" data-save-defaults [sfeBusy]="saving()" [disabled]="saving() || !reading.changed()"
                        title="Start from these settings (both kinds of text, and the colours) in every browser you sign in from" (click)="saveDefaults()">Save as my default</button>
            }
        </footer>
    `,
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

    protected set<K extends 'font' | 'lineHeight' | 'width' | 'padding' | 'wrap' | 'lineNumbers'>(key: K, value: unknown): void {
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
        if (target && !this.element.contains(target) && !target.closest?.('[data-reading-toggle]'))
            this.closed.emit();
    }
}
