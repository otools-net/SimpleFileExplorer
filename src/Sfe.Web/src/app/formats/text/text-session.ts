import { computed, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { Api, ApiError } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { isFileConflict, isSaved, LibraryItem, TextFile, TextToSave } from '../../core/models';
import { parentPath } from '../../core/paths';
import { ToastService } from '../../core/toast.service';
import { FILE_VIEW_HOST } from '../formats';
import { TextEditor } from './codemirror';

/** What stops a save, shown as a bar over the text: the file changed elsewhere, its encoding can't hold the text, or the copy's name asked for. */
export type TextProblem = { kind: 'changed' } | { kind: 'not-encodable'; message: string } | { kind: 'copy'; error: string | null };

export const UNSAVED_QUESTION = 'Your changes to this file are not saved. Leave without saving them?';

/** "notes.txt" → "notes (copy).txt": the name a copy is first offered. */
export function copyName(name: string): string {
    const dot = name.lastIndexOf('.');
    return dot > 0 ? `${name.slice(0, dot)} (copy)${name.slice(dot)}` : `${name} (copy)`;
}

/** An editing session id, as the server wants it (letters, digits): crypto.randomUUID needs a secure context. */
function sessionId(): string {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
}

const LINE_BREAKS: Readonly<Record<string, string>> = { lf: '\n', crlf: '\r\n', cr: '\r' };

/**
 * Editing a text file in a view (agents/docs/features/text-editor.md), one per view component (its `providers`):
 * Edit, Save over the file when the rights allow it (Replace's rule, told by the server as `access`), else Save a copy
 * or Download. A save names the version it started from; the file changed elsewhere asks what to do. Unsaved changes
 * are asked about before the file is left (the host's guard, and the browser's own question when the page goes).
 */
@Injectable()
export class TextSession {
    private readonly api = inject(Api);
    private readonly toasts = inject(ToastService);
    private readonly config = inject(ConfigService);
    private readonly host = inject(FILE_VIEW_HOST);

    /** One per view: the server keeps the previous version once per session. */
    private readonly session = sessionId();
    private item: LibraryItem | null = null;
    private editor: TextEditor | null = null;
    /** The person agreed to lose the changes: further questions (the route's, after the viewer's) pass. */
    private discarded = false;
    private releaseGuard: (() => void) | null = null;

    readonly file = signal<TextFile | null>(null);
    readonly editing = signal(false);
    readonly dirty = signal(false);
    readonly saving = signal(false);
    readonly problem = signal<TextProblem | null>(null);
    /** The name typed for a copy. */
    readonly copyName = signal('');
    /** Each change of the text (for a view that shows it otherwise, as Markdown's). */
    readonly revision = signal(0);
    /** The encoding a save writes: the file's, until "Save as UTF-8". */
    private readonly encoding = signal<string | null>(null);

    readonly access = computed(() => this.file()?.access ?? null);
    /** Something can be done with changes: save over it, save a copy or download them. */
    readonly canEdit = computed(() => {
        const access = this.access();
        return Boolean(access && (access.overwrite || access.saveCopy || access.download));
    });
    readonly canSave = computed(() => this.access()?.overwrite ?? false);

    constructor() {
        const onUnload = (event: BeforeUnloadEvent) => {
            if (this.dirty() && !this.discarded)
                event.preventDefault();
        };
        window.addEventListener('beforeunload', onUnload);
        inject(DestroyRef).onDestroy(() => {
            window.removeEventListener('beforeunload', onUnload);
            this.releaseGuard?.();
        });
    }

    /** The file read, before its editor exists. */
    attach(item: LibraryItem, file: TextFile): void {
        this.item = item;
        this.file.set(file);
        this.encoding.set(file.encoding);
        this.releaseGuard ??= this.host.guard?.(() => this.canLeave()) ?? null;
    }

    /** What a new editor of this file takes, so that it can be made editable when the person already asked. */
    editorOptions(): { editable: boolean; onChange: (dirty: boolean) => void } {
        return { editable: this.editing(), onChange: dirty => this.changed(dirty) };
    }

    setEditor(editor: TextEditor): void {
        this.editor = editor;
        if (this.editing() && !editor.editable)
            editor.setEditable(true);
    }

    /** The file may be left: nothing unsaved, or the person agrees to lose it. */
    canLeave(): boolean {
        if (!this.dirty() || this.discarded)
            return true;
        this.discarded = confirm(UNSAVED_QUESTION);
        return this.discarded;
    }

    startEdit(): void {
        if (!this.canEdit() || this.editing())
            return;
        this.editing.set(true);
        this.editor?.setEditable(true);
        this.editor?.focus();
    }

    /** Back to reading; unsaved changes are given up (when the person agrees). */
    stopEdit(): void {
        if (this.dirty() && !confirm('Give up your changes to this file?'))
            return;
        if (this.dirty())
            this.editor?.replace(this.file()!.text);
        this.dirty.set(false);
        this.problem.set(null);
        this.editing.set(false);
        this.editor?.setEditable(false);
    }

    private changed(dirty: boolean): void {
        this.dirty.set(dirty);
        this.discarded = false;
        this.revision.update(value => value + 1);
        if (this.problem()?.kind === 'not-encodable')
            this.problem.set(null);
    }

    private toSave(): TextToSave {
        const file = this.file()!;
        const lineEnding = file.saveLineEnding ?? (file.lineEnding === 'mixed' ? 'crlf' : file.lineEnding);
        return { text: this.editor!.text(), encoding: this.encoding() ?? file.encoding, lineEnding };
    }

    /** Ctrl+S: over the file when the rights allow it, else a copy. `force`: over whatever is there now. */
    async save(force = false): Promise<void> {
        const file = this.file();
        if (!file || !this.editor || !this.item || this.saving())
            return;
        if (!this.canSave()) {
            if (this.access()?.saveCopy)
                this.openCopy();
            else
                this.toasts.show('You may not save over this file: download your text instead.', { error: true });
            return;
        }
        const text = this.toSave();
        this.saving.set(true);
        try {
            const result = await this.api.saveText(this.item.path, text, this.session, force ? '*' : file.version);
            if (!isSaved(result)) {
                this.toasts.error(result.detail ?? 'It could not be saved.');
                return;
            }
            this.file.set({ ...file, text: text.text, encoding: text.encoding, version: result.version, size: result.size, modified: result.modified, lineEnding: text.lineEnding });
            this.editor.markSaved();
            this.dirty.set(this.editor.dirty);
            this.problem.set(null);
            this.toasts.show(result.trashId ? 'Saved. The version before is in the trash.' : 'Saved.', { timeout: 3000 });
        } catch (error) {
            if (error instanceof ApiError && error.status === 412)
                this.problem.set({ kind: 'changed' });
            else if (error instanceof ApiError && error.status === 422)
                this.problem.set({ kind: 'not-encodable', message: error.message });
            else
                this.toasts.error(`It could not be saved: ${(error as Error).message}`);
        } finally {
            this.saving.set(false);
        }
    }

    /** The text can't be written in the file's encoding: in UTF-8 then (which holds everything). */
    async saveAsUtf8(): Promise<void> {
        this.encoding.set(this.file()?.encoding === 'utf-8-bom' ? 'utf-8-bom' : 'utf-8');
        await this.save();
    }

    /** The file as it is now on disk, in place of the changes. */
    async reload(): Promise<void> {
        if (!this.item)
            return;
        try {
            const file = await this.api.textFile(this.item.path);
            this.file.set(file);
            this.encoding.set(file.encoding);
            this.editor?.replace(file.text);
            this.dirty.set(false);
            this.problem.set(null);
        } catch (error) {
            this.toasts.error(`It could not be read again: ${(error as Error).message}`);
        }
    }

    openCopy(): void {
        if (!this.item)
            return;
        this.copyName.set(copyName(this.item.name));
        this.problem.set({ kind: 'copy', error: null });
    }

    /** The text as a new file next to this one, which then opens ready to be changed further. */
    async saveCopy(name = this.copyName()): Promise<void> {
        const item = this.item;
        if (!item || !this.editor || this.saving() || !name.trim())
            return;
        const text = this.toSave();
        this.saving.set(true);
        try {
            const result = await this.api.saveTextCopy(parentPath(item.path), name.trim(), text);
            if (isFileConflict(result)) {
                this.problem.set({ kind: 'copy', error: result.detail ?? 'It could not be saved.' });
                return;
            }
            // Its changes are in the copy now: this file may be left without asking.
            this.discarded = true;
            this.problem.set(null);
            this.toasts.show(`Saved as “${result.name}”.`, { timeout: 4000 });
            if (this.host.open)
                this.host.open(result, { edit: true });
            else
                location.assign(`${this.config.urls()!.view(result.path)}&edit=1`);
        } catch (error) {
            const message = error instanceof ApiError && error.status === 422 ? error.message : `It could not be saved: ${(error as Error).message}`;
            this.problem.set({ kind: 'copy', error: message });
        } finally {
            this.saving.set(false);
        }
    }

    /** The text as it is now, to the browser's downloads (UTF-8, the file's line endings). */
    download(): void {
        if (!this.item || !this.editor)
            return;
        const text = this.toSave();
        const blob = new Blob([text.text.replace(/\n/g, LINE_BREAKS[text.lineEnding] ?? '\n')], { type: 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = this.item.name;
        link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    /** Ctrl+S anywhere in the view's page (never the browser's "save page"), E to start editing. True when used. */
    handleKey(event: KeyboardEvent): boolean {
        if (event.defaultPrevented || !this.file())
            return false;
        const key = event.key.toLowerCase();
        if ((event.ctrlKey || event.metaKey) && !event.altKey && key === 's') {
            event.preventDefault();
            if (!this.editing())
                return true;
            if (event.shiftKey) {
                if (this.access()?.saveCopy)
                    this.openCopy();
            } else if (this.dirty())
                void this.save();
            return true;
        }
        const target = event.target as Element | null;
        // The read-only editor takes no typing: E there edits (its find panel's fields do take it).
        const typing = target?.closest?.('input, textarea, select');
        if (key === 'e' && !event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey && !typing && !this.editing() && this.canEdit()) {
            event.preventDefault();
            this.startEdit();
            return true;
        }
        return false;
    }
}
