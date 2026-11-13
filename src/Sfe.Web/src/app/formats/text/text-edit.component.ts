import { ChangeDetectionStrategy, Component, effect, ElementRef, inject, viewChild } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';
import { TextSession } from './text-session';

/**
 * The editing buttons of a text view's toolbar (agents/docs/features/text-editor.md): Edit, then Save (when the rights
 * allow saving over the file), Save a copy, Download and Done. Uses the view's `TextSession`.
 */
@Component({
    selector: 'sfe-text-edit-actions',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'text-edit-actions' },
    template: `
        @if (session.canEdit()) {
            @if (!session.editing()) {
                <button type="button" class="text-action" data-edit title="Change the text (E)" (click)="session.startEdit()"><svg sfeIcon="edit"></svg> Edit</button>
            } @else {
                @if (session.canSave()) {
                    <!-- The same label while saving: a wider "Saving…" made the toolbar (an overflow row) measure itself again in a
                         loop on phones (ResizeObserver errors); the state shows as a busy button instead. -->
                    <button type="button" class="text-action primary" data-save [class.saving]="session.saving()" [attr.aria-busy]="session.saving() || null"
                            [title]="session.saving() ? 'Saving…' : 'Save over the file (Ctrl+S)'" [disabled]="!session.dirty() || session.saving()" (click)="session.save()">
                        <svg sfeIcon="save"></svg> Save</button>
                }
                @if (session.access()?.saveCopy) {
                    <button type="button" class="text-action" data-save-copy title="Save the text as a new file in this folder (Ctrl+Shift+S)" [disabled]="session.saving()" (click)="session.openCopy()">
                        <svg sfeIcon="copy"></svg> <span class="text-action-label">Save a copy</span></button>
                }
                @if (session.access()?.download) {
                    <button type="button" class="icon-button" data-download-text title="Download the text as it is now" aria-label="Download the text" (click)="session.download()"><svg sfeIcon="download"></svg></button>
                }
                <button type="button" class="text-action" data-done title="Stop editing" (click)="session.stopEdit()"><svg sfeIcon="check"></svg> <span class="text-action-label">Done</span></button>
            }
        }
    `,
})
export class TextEditActionsComponent {
    protected readonly session = inject(TextSession);
}

/**
 * Under the toolbar while editing: why a save waits (the file changed elsewhere, characters its encoding can't hold),
 * the name of a copy, or that this file can't be saved over here.
 */
@Component({
    selector: 'sfe-text-edit-bar',
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './text-edit-bar.component.html',
})
export class TextEditBarComponent {
    protected readonly session = inject(TextSession);
    private readonly copyInput = viewChild<ElementRef<HTMLInputElement>>('copyInput');

    constructor() {
        // The copy's name selected up to its extension, ready to be typed over.
        effect(() => {
            const input = this.copyInput()?.nativeElement;
            if (!input) return;
            const dot = input.value.lastIndexOf('.');
            input.focus();
            input.setSelectionRange(0, dot > 0 ? dot : input.value.length);
        });
    }
}
