import { computed, DestroyRef, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { ConfigService } from './config.service';
import { readPreference, writePreference } from './preferences';
import type { ToastAction, ToastLevel } from './toast.service';

/** How many notifications a browser keeps per account and space, and for how long. */
export const NOTIFICATION_LIMIT = 200;
export const NOTIFICATION_DAYS = 30;
const DAY = 86_400_000;
const KEY = 'sfe.notifications';

/** A toast kept (agents/docs/features/notifications.md). */
export interface Notification {
    /** Unique across the tabs of the browser. */
    id: string;
    message: string;
    level: ToastLevel;
    /** When it came, in milliseconds since 1970. */
    at: number;
    read: boolean;
    /** Its actions that open a page: kept after a reload. */
    links: StoredLink[];
}

interface StoredLink {
    label: string;
    title: string;
    link: string;
}

/** A notification as listed: its actions while this tab lasts, else its links. */
export interface NotificationView extends Notification {
    actions: ToastAction[];
}

/**
 * The notification centre: every toast of this browser kept (`ToastService` records them), newest first, at most
 * `NOTIFICATION_LIMIT` for `NOTIFICATION_DAYS` days, per account and space (localStorage; other tabs follow through
 * the `storage` event). A warning or an error, or anything that came while the page was hidden, is unread until the
 * list is opened; a message seen in its toast is not. Actions that only run code (Undo) last as long as the tab.
 */
@Injectable({ providedIn: 'root' })
export class Notifications {
    private readonly config = inject(ConfigService);
    private readonly stored = signal<readonly Notification[]>([]);
    /** The actions of the notifications made in this tab, by id. */
    private readonly actions = new Map<string, ToastAction[]>();
    /** Where they are kept: null before the configuration is known (kept in memory, saved once it is). */
    private readonly key = computed(() => {
        const config = this.config.config();
        return config ? `${KEY}.${config.tenant?.id ?? 0}.${config.user?.id ?? 0}` : null;
    });

    readonly list = computed<NotificationView[]>(() =>
        this.stored().map((item) => ({ ...item, actions: this.actionsOf(item) })),
    );
    readonly unread = computed(() => this.stored().filter((item) => !item.read).length);

    constructor() {
        // The first key keeps what came before it; another account or space starts from its own.
        let previous: string | null = null;
        effect(() => {
            const key = this.key();
            if (!key || key === previous) return;
            untracked(() => this.stored.set(prune([...(previous ? [] : this.stored()), ...load(key)])));
            previous = key;
            untracked(() => this.save());
        });
        const onStorage = (event: StorageEvent) => {
            const key = this.key();
            if (key && event.key === key) this.stored.set(load(key));
        };
        window.addEventListener('storage', onStorage);
        inject(DestroyRef).onDestroy(() => window.removeEventListener('storage', onStorage));
    }

    /** Keeps a toast's message (`ToastService.show`). */
    record(message: string, level: ToastLevel, actions: ToastAction[], now = Date.now()): void {
        const id = `${now.toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
        const read = (level === 'info' || level === 'success') && document.visibilityState === 'visible';
        const links = actions.flatMap(({ label, title, link }) => (link ? [{ label, title, link }] : []));
        if (actions.length) this.actions.set(id, actions);
        this.update((list) => [{ id, message, level, at: now, read, links }, ...list]);
    }

    /** The list was looked at: nothing is unread any more. */
    markAllRead(): void {
        if (this.unread()) this.update((list) => list.map((item) => (item.read ? item : { ...item, read: true })));
    }

    remove(id: string): void {
        this.actions.delete(id);
        this.update((list) => list.filter((item) => item.id !== id));
    }

    clear(): void {
        this.actions.clear();
        this.update(() => []);
    }

    private actionsOf(item: Notification): ToastAction[] {
        return this.actions.get(item.id) ?? item.links;
    }

    private update(change: (list: readonly Notification[]) => readonly Notification[]): void {
        this.stored.update((list) => prune(change(list)));
        this.save();
    }

    private save(): void {
        const key = this.key();
        if (key) writePreference(key, JSON.stringify(this.stored()));
    }
}

/** Newest first, without doubles (the same id from memory and storage), the old and the extra left out. */
function prune(list: readonly Notification[], now = Date.now()): Notification[] {
    const seen = new Set<string>();
    return [...list]
        .sort((a, b) => b.at - a.at)
        .filter((item) => item.at > now - NOTIFICATION_DAYS * DAY && !seen.has(item.id) && seen.add(item.id))
        .slice(0, NOTIFICATION_LIMIT);
}

function load(key: string): Notification[] {
    try {
        const stored: unknown = JSON.parse(readPreference(key, '[]'));
        return Array.isArray(stored) ? stored.filter(isNotification) : [];
    } catch {
        return [];
    }
}

function isNotification(value: unknown): value is Notification {
    const item = value as Notification | null;
    return (
        typeof item?.id === 'string' &&
        typeof item.message === 'string' &&
        typeof item.at === 'number' &&
        Array.isArray(item.links)
    );
}
