import { inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Notifications } from './notifications';

/** How a toast reads: a plain message, something done, something to look at, something that failed. */
export type ToastLevel = 'info' | 'success' | 'warning' | 'error';

/**
 * A button of a toast (Undo, Open): clicking it runs `run`, or opens `link`, and closes the toast. An action with a
 * `link` (a page of the app, as the router serializes it) is kept with its notification after a reload; one with only
 * `run` lasts as long as the tab.
 */
export interface ToastAction {
    label: string;
    title: string;
    run?: () => void;
    link?: string;
    /** True once it can't be done any more (the action was undone otherwise): hidden in the notification centre. */
    gone?: () => boolean;
}

export interface Toast {
    id: number;
    message: string;
    level: ToastLevel;
    /** The first of `actions`. */
    action?: ToastAction;
    /** Its buttons (Undo, Open). */
    actions: ToastAction[];
}

export interface ToastOptions {
    level?: ToastLevel;
    timeout?: number;
    action?: ToastAction;
    actions?: ToastAction[];
}

/**
 * Short messages in the corner of the page (see ToastsComponent), each removed after its timeout. Every toast is also
 * kept by the notification centre (`Notifications`).
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
    private readonly router = inject(Router);
    private readonly notifications = inject(Notifications);
    private nextId = 0;
    private readonly list = signal<Toast[]>([]);
    readonly toasts = this.list.asReadonly();

    show(message: string, { level = 'info', timeout = 4500, action, actions = [] }: ToastOptions = {}): void {
        const all = action ? [action, ...actions] : actions;
        const toast: Toast = { id: ++this.nextId, message, level, action: all[0], actions: all };
        this.list.update((list) => [...list, toast]);
        this.notifications.record(message, level, all);
        setTimeout(() => this.dismiss(toast.id), timeout);
    }

    /** Something done (renamed, moved, saved). */
    success(message: string, options: Omit<ToastOptions, 'level'> = {}): void {
        this.show(message, { ...options, level: 'success' });
    }

    /** Something done in part, or to look at. */
    warning(message: string, options: Omit<ToastOptions, 'level'> = {}): void {
        this.show(message, { ...options, level: 'warning' });
    }

    error(message: string, timeout?: number): void {
        this.show(message, { level: 'error', timeout });
    }

    dismiss(id: number): void {
        this.list.update((list) => list.filter((t) => t.id !== id));
    }

    /** Runs one of the toast's actions (its first by default) and closes it. */
    act(toast: Toast, action = toast.action): void {
        this.dismiss(toast.id);
        if (action) this.perform(action);
    }

    /** Runs `action`, or opens its link. */
    perform(action: ToastAction): void {
        if (action.run) action.run();
        else if (action.link) void this.router.navigateByUrl(action.link);
    }
}
