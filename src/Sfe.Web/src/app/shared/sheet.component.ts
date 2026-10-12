import { ChangeDetectionStrategy, Component, ElementRef, input, output, signal, viewChild } from '@angular/core';
import { IconComponent } from './icon.component';

/**
 * Dialog card with a title, a scrolling body and a footer for buttons. Content goes in the body; elements marked
 * `sheetFooter` go in the footer and `sheetTabs` between the title and the body. `open()` shows it as a modal.
 */
@Component({
    selector: 'sfe-sheet',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <dialog #dialog class="sheet" [class]="variant()" [class.wide]="wide()"
                (pointerdown)="onPointerDown($event)"
                (click)="backdropClick($event)"
                (close)="onClose()">
            <header class="sheet-head">
                <div class="sheet-title">
                    <h2>{{ heading() }}</h2>
                    @if (subtitle()) {
                        <p class="sheet-subtitle">{{ subtitle() }}</p>
                    }
                </div>
                <button type="button" class="icon-button ghost small" aria-label="Close" title="Close (Esc)" (click)="close()">
                    <svg sfeIcon="close"></svg>
                </button>
            </header>
            <ng-content select="[sheetTabs]" />
            <div #body class="sheet-body">
                <ng-content />
            </div>
            <footer class="sheet-foot">
                <ng-content select="[sheetFooter]" />
            </footer>
        </dialog>
    `,
})
export class SheetComponent {
    readonly heading = input.required<string>();
    readonly subtitle = input<string | null>(null);
    readonly wide = input(false);
    /** Extra classes of the dialog, e.g. "settings". */
    readonly variant = input('');
    readonly closed = output<void>();

    private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
    private readonly body = viewChild.required<ElementRef<HTMLElement>>('body');
    private readonly openState = signal(false);
    readonly isOpen = this.openState.asReadonly();
    protected pressedBackdrop = false;

    open(): void {
        const dialog = this.dialog().nativeElement;
        if (!dialog.open)
            dialog.showModal();
        this.openState.set(true);
    }

    close(): void {
        this.dialog().nativeElement.close();
    }

    scrollToTop(): void {
        this.body().nativeElement.scrollTop = 0;
    }

    /**
     * Remembers whether a press started on the backdrop. A method, not an assignment in the template: a handler
     * expression that evaluates to false makes Angular call preventDefault(), which here cancelled the mouse events
     * of every press inside the card (native dropdowns no longer opened).
     */
    protected onPointerDown(event: PointerEvent): void {
        this.pressedBackdrop = event.target === this.dialog().nativeElement;
    }

    /**
     * A click on the backdrop (outside the card) closes the dialog. Only when it also started there: a click is
     * dispatched to the common ancestor of the press and the release, so a press on a button whose dialog then
     * shrinks (e.g. tag suggestions collapsing) would otherwise end on the backdrop and close it.
     */
    protected backdropClick(event: MouseEvent): void {
        if (event.target === this.dialog().nativeElement && this.pressedBackdrop)
            this.close();
        this.pressedBackdrop = false;
    }

    protected onClose(): void {
        this.openState.set(false);
        this.closed.emit();
    }
}
