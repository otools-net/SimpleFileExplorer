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
    template: `
        @if (session.problem(); as problem) {
            @switch (problem.kind) {
                @case ('changed') {
                    <div class="text-bar warning" role="alert" data-bar="changed">
                        <span>This file was changed elsewhere since you opened it.</span>
                        <button type="button" class="button" title="Read it as it is now: your changes are lost" (click)="session.reload()">Reload</button>
                        @if (session.canSave()) {
                            <button type="button" class="button" title="Save your text over the other changes" (click)="session.save(true)">Overwrite</button>
                        }
                        @if (session.access()?.saveCopy) {
                            <button type="button" class="button" (click)="session.openCopy()">Save a copy</button>
                        }
                        <button type="button" class="link-button" (click)="session.problem.set(null)">Keep editing</button>
                    </div>
                }
                @case ('disk') {
                    <div class="text-bar warning" role="alert" data-bar="disk">
                        <span>{{ session.dirty() ? 'This file was changed elsewhere: reloading it loses your changes.' : 'This file was changed elsewhere since it was opened.' }}</span>
                        <button type="button" class="button" title="Read it as it is now" (click)="session.reload()">Reload</button>
                        <button type="button" class="link-button" title="Keep the text shown (a save asks again)" (click)="session.ignoreDisk()">{{ session.editing() ? 'Keep editing' : 'Keep this one' }}</button>
                    </div>
                }
                @case ('not-encodable') {
                    <div class="text-bar warning" role="alert" data-bar="not-encodable">
                        <span>{{ problem.message }}</span>
                        <button type="button" class="button" (click)="session.saveAsUtf8()">Save as UTF-8</button>
                        <button type="button" class="link-button" (click)="session.problem.set(null)">Keep editing</button>
                    </div>
                }
                @case ('copy') {
                    <form class="text-bar" data-bar="copy" (submit)="$event.preventDefault(); session.saveCopy()">
                        <label>Save a copy as <input #copyInput type="text" name="name" maxlength="255" autocomplete="off" spellcheck="false"
                                                     [value]="session.copyName()" (input)="session.copyName.set(copyInput.value)"></label>
                        <button type="submit" class="button primary" [disabled]="session.saving() || !session.copyName().trim()">Save</button>
                        <button type="button" class="link-button" (click)="session.problem.set(null)">Cancel</button>
                        @if (problem.error) {
                            <span class="text-bar-error" role="alert">{{ problem.error }}</span>
                        }
                    </form>
                }
            }
        } @else if (session.editing() && !session.canSave()) {
            <div class="text-bar" data-bar="copy-only">
                <span>{{ session.access()?.saveCopy ? 'You may not save over this file: save a copy of your text, or download it.' : 'You may not save here: download your text to keep it.' }}</span>
            </div>
        }
    `,
})
export class TextEditBarComponent {
    protected readonly session = inject(TextSession);
    private readonly copyInput = viewChild<ElementRef<HTMLInputElement>>('copyInput');

    constructor() {
        // The copy's name selected up to its extension, ready to be typed over.
        effect(() => {
            const input = this.copyInput()?.nativeElement;
            if (!input)
                return;
            const dot = input.value.lastIndexOf('.');
            input.focus();
            input.setSelectionRange(0, dot > 0 ? dot : input.value.length);
        });
    }
}
