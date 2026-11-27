import {
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    effect,
    inject,
    signal,
    untracked,
} from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { Rights } from '../../core/rights';
import { StorageUsage } from '../library.models';
import { ChangeFeed } from '../change-feed.service';
import { ListingStore } from '../listing.store';
import { storageHint, storageLevel, storageLine, usedShare } from './storage-texts';

/** How often the space is read again while the page is shown (the server keeps it two minutes). */
export const STORAGE_REFRESH_MS = 5 * 60_000;

/** After a change made through SFE, the space is read again this long after the changes stop (a job's many). */
export const STORAGE_CHANGE_DELAY_MS = 2_000;

/**
 * The sidebar's storage bar: the space used and free where the files listed are kept, the disk holding the library
 * folder shown (a junction in the library may lead to another disk) or the account of the connection shown; orange
 * from 85 % used, red from 95 %. Read when the place or the library folder changes, after files were changed there
 * through SFE (`ChangeFeed.spaceChanges`), then every few minutes while the page is shown; hidden when it can't be
 * read (a connection to sign in to again).
 */
@Component({
    selector: 'sfe-storage-bar',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: {
        class: 'storage-bar',
        '[hidden]': '!usage()',
        '[class.nearly-full]': "level() === 'nearly-full'",
        '[class.full]': "level() === 'full'",
        '[attr.title]': 'hint()',
        '(document:visibilitychange)': 'readIfDue()',
    },
    template: `
        @if (percent(); as percent) {
            <div
                class="storage-meter"
                role="meter"
                aria-valuemin="0"
                aria-valuemax="100"
                [attr.aria-valuenow]="percent"
                [attr.aria-label]="hint()"
            >
                <span [style.width.%]="percent"></span>
            </div>
        }
        <span class="storage-line">{{ line() }}</span>
    `,
})
export class StorageBarComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly store = inject(ListingStore);
    /** The connection shown; null in the library. */
    private readonly connection = computed(() => this.config.place()?.id ?? null);
    /** The library folder shown ('' for its root, a connection or a search everywhere). */
    private readonly folder = computed(() => {
        const route = this.store.route();
        return this.connection() !== null || route.all ? '' : route.path;
    });
    private readAt = 0;
    private sequence = 0;
    private changeTimer: ReturnType<typeof setTimeout> | undefined;

    protected readonly usage = signal<StorageUsage | null>(null);
    private readonly share = computed(() => {
        const usage = this.usage();
        return usage ? usedShare(usage) : null;
    });
    protected readonly level = computed(() => storageLevel(this.share()));
    /** The share used in whole percent, at least 1 so that a little is seen; null without a total. */
    protected readonly percent = computed(() => {
        const share = this.share();
        return share === null ? null : Math.max(1, Math.round(share * 100));
    });
    protected readonly line = computed(() => {
        const usage = this.usage();
        return usage ? storageLine(usage) : '';
    });
    protected readonly hint = computed(() => {
        const usage = this.usage();
        return usage ? storageHint(usage, this.connection() === null ? null : (this.config.place()?.name ?? '')) : '';
    });

    constructor() {
        effect(() => {
            const connection = this.connection();
            const folder = this.folder();
            untracked(() => void this.read(connection, folder));
        });
        const feed = inject(ChangeFeed);
        let seen = untracked(() => feed.spaceChanges());
        effect(() => {
            const changes = feed.spaceChanges();
            if (changes === seen) return;
            seen = changes;
            clearTimeout(this.changeTimer);
            this.changeTimer = setTimeout(
                () => void this.read(this.connection(), this.folder()),
                STORAGE_CHANGE_DELAY_MS,
            );
        });
        const timer = setInterval(() => this.readIfDue(), STORAGE_REFRESH_MS);
        inject(DestroyRef).onDestroy(() => {
            clearInterval(timer);
            clearTimeout(this.changeTimer);
        });
    }

    /** Read again when the page is shown and the last read is old; nothing while it is hidden. */
    protected readIfDue(): void {
        if (document.visibilityState === 'visible' && Date.now() - this.readAt >= STORAGE_REFRESH_MS)
            void this.read(this.connection(), this.folder());
    }

    private async read(connection: number | null, folder: string): Promise<void> {
        const request = ++this.sequence;
        this.readAt = Date.now();
        if (connection === null && !this.config.can(Rights.browse)) {
            this.usage.set(null);
            return;
        }
        try {
            const usage = await this.api.storage(connection, folder);
            if (request === this.sequence) this.usage.set(usage);
        } catch {
            if (request === this.sequence) this.usage.set(null);
        }
    }
}
