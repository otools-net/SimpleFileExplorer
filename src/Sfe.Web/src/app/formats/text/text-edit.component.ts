import { ChangeDetectionStrategy, Component, effect, ElementRef, inject, viewChild } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';
import { TextSession } from './text-session';
import { textEditTexts } from './text-texts';

/**
 * The editing buttons of a text view's toolbar (agents/docs/features/text-editor.md): Edit, then Save (when the rights
 * allow saving over the file), Save a copy, Download and Done. Uses the view's `TextSession`.
 */
@Component({
    selector: 'sfe-text-edit-actions',
    imports: [IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'text-edit-actions' },
    templateUrl: './text-edit.component.html',
})
export class TextEditActionsComponent {
    protected readonly session = inject(TextSession);
    protected readonly texts = textEditTexts;
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
