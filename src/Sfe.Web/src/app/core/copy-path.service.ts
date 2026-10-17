import { computed, inject, Injectable, signal } from '@angular/core';
import { ConfigService } from './config.service';
import { ToastService } from './toast.service';

/**
 * Copying the full path on the server of a folder or file (`Browse:CopyPaths`): the root comes from /api/config
 * `rootPath`, null when the option is off.
 */
@Injectable({ providedIn: 'root' })
export class CopyPathService {
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);

    readonly enabled = computed(() => Boolean(this.config.config()?.rootPath));

    private readonly copiedPaths = signal<ReadonlySet<string>>(new Set());
    /**
     * The API paths copied since the page was loaded, which the listing outlines so one can tell which items were
     * already copied. Kept in memory only, on purpose: a reload forgets them.
     */
    readonly copied = this.copiedPaths.asReadonly();

    /** The full path of a root-relative API path, with the separator of the server (the one its root ends with). */
    fullPath(path: string): string {
        const root = this.config.value.rootPath ?? '';
        if (!path) {
            // The root itself without its separator, unless that leaves a bare drive ("C:") or nothing ("/").
            const trimmed = root.slice(0, -1);
            return trimmed && !trimmed.endsWith(':') ? trimmed : root;
        }
        return root + path.split('/').join(root.slice(-1));
    }

    async copy(path: string): Promise<void> {
        const text = this.fullPath(path);
        try {
            await copyText(text);
            this.copiedPaths.update(paths => new Set(paths).add(path));
            this.toasts.show(`Copied ${text}`);
        } catch {
            this.toasts.error(`Could not copy ${text}`);
        }
    }
}

/**
 * The asynchronous clipboard only exists on secure pages (https or localhost), and may still be refused (no focus,
 * permission policy); otherwise the text is copied the old way, through a selected text box.
 */
export async function copyText(text: string): Promise<void> {
    if (window.isSecureContext && navigator.clipboard) {
        try {
            await navigator.clipboard.writeText(text);
            return;
        } catch {
            // the old way below
        }
    }
    // Inside a modal dialog, the rest of the page is inert: the text box must be in the dialog to be selected.
    const box = document.createElement('textarea');
    box.readOnly = true;
    box.style.cssText = 'position: fixed; top: 0; left: 0; opacity: 0;';
    box.value = text;
    (document.querySelector('dialog[open]') ?? document.body).append(box);
    try {
        box.select();
        if (!document.execCommand('copy'))
            throw new Error('Copy refused');
    } finally {
        box.remove();
    }
}
