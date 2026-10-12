import { Injectable, signal } from '@angular/core';

export interface Toast {
    id: number;
    message: string;
    error: boolean;
}

/** Short messages in the corner of the page (see ToastsComponent), each removed after its timeout. */
@Injectable({ providedIn: 'root' })
export class ToastService {
    private nextId = 0;
    private readonly list = signal<Toast[]>([]);
    readonly toasts = this.list.asReadonly();

    show(message: string, { error = false, timeout = 4500 }: { error?: boolean; timeout?: number } = {}): void {
        const toast = { id: ++this.nextId, message, error };
        this.list.update(list => [...list, toast]);
        setTimeout(() => this.list.update(list => list.filter(t => t !== toast)), timeout);
    }

    error(message: string, timeout?: number): void {
        this.show(message, { error: true, timeout });
    }
}
