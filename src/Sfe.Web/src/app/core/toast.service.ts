import { Injectable, signal } from '@angular/core';

/** A button of a toast (Undo): clicking it runs `run` and closes the toast. */
export interface ToastAction {
    label: string;
    title: string;
    run: () => void;
}

export interface Toast {
    id: number;
    message: string;
    error: boolean;
    /** The first of `actions`. */
    action?: ToastAction;
    /** Its buttons (Undo, Open). */
    actions: ToastAction[];
}

/** Short messages in the corner of the page (see ToastsComponent), each removed after its timeout. */
@Injectable({ providedIn: 'root' })
export class ToastService {
    private nextId = 0;
    private readonly list = signal<Toast[]>([]);
    readonly toasts = this.list.asReadonly();

    show(message: string, { error = false, timeout = 4500, action, actions = [] }: { error?: boolean; timeout?: number; action?: ToastAction; actions?: ToastAction[] } = {}): void {
        const all = action ? [action, ...actions] : actions;
        const toast: Toast = { id: ++this.nextId, message, error, action: all[0], actions: all };
        this.list.update(list => [...list, toast]);
        setTimeout(() => this.dismiss(toast.id), timeout);
    }

    error(message: string, timeout?: number): void {
        this.show(message, { error: true, timeout });
    }

    dismiss(id: number): void {
        this.list.update(list => list.filter(t => t.id !== id));
    }

    /** Runs one of the toast's actions (its first by default) and closes it. */
    act(toast: Toast, action = toast.action): void {
        this.dismiss(toast.id);
        action?.run();
    }
}
