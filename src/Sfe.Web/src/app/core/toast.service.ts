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
    action?: ToastAction;
}

/** Short messages in the corner of the page (see ToastsComponent), each removed after its timeout. */
@Injectable({ providedIn: 'root' })
export class ToastService {
    private nextId = 0;
    private readonly list = signal<Toast[]>([]);
    readonly toasts = this.list.asReadonly();

    show(message: string, { error = false, timeout = 4500, action }: { error?: boolean; timeout?: number; action?: ToastAction } = {}): void {
        const toast: Toast = { id: ++this.nextId, message, error, action };
        this.list.update(list => [...list, toast]);
        setTimeout(() => this.dismiss(toast.id), timeout);
    }

    error(message: string, timeout?: number): void {
        this.show(message, { error: true, timeout });
    }

    dismiss(id: number): void {
        this.list.update(list => list.filter(t => t.id !== id));
    }

    /** Runs the toast's action and closes it. */
    act(toast: Toast): void {
        this.dismiss(toast.id);
        toast.action?.run();
    }
}
