import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { joinParts } from '../core/format';
import { icu } from '../core/i18n/icu';
import { LogEntry, LogLevelName, LogSearchResult } from './log.models';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import {
    hasFilters,
    LEVEL_NAMES,
    LOG_LEVELS,
    LogFilters,
    NO_FILTERS,
    shortCategory,
    SpaceFilter,
    toQuery,
} from './logs';

const TYPING_MS = 400;
const timeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'medium' });

/**
 * Administration › Logs: the server's log, newest first, searched by text, severity, days and (server administrators)
 * space. An entry opens on its logger's full name and its exception. Pages of older entries are added at the end.
 */
@Component({
    selector: 'sfe-logs-section',
    imports: [BusyDirective, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'logs-section' },
    templateUrl: './logs-section.component.html',
})
export class LogsSectionComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);

    readonly openSettings = output<void>();

    protected readonly levels = LOG_LEVELS;
    protected readonly levelNames = LEVEL_NAMES;
    protected readonly short = shortCategory;
    protected readonly entryTitles = {
        open: $localize`Show less of this entry`,
        closed: $localize`Show the whole entry`,
    };

    protected readonly filters = signal<LogFilters>(NO_FILTERS);
    protected readonly result = signal<LogSearchResult | null>(null);
    protected readonly entries = signal<LogEntry[]>([]);
    protected readonly next = signal<number | null>(null);
    protected readonly loaded = signal(false);
    protected readonly loading = signal(false);
    protected readonly paging = signal(false);
    protected readonly error = signal<string | null>(null);
    protected readonly openId = signal<number | null>(null);

    protected readonly filtered = computed(() => hasFilters(this.filters()));
    protected readonly allSpaces = computed(() => this.result()?.allSpaces ?? false);
    protected readonly spaces = computed(() => this.config.config()?.tenants ?? []);
    protected readonly keptText = computed(() => {
        const result = this.result();
        if (!result) return '';
        const kept = icu(
            $localize`{days, plural, =0 {Entries are kept forever.} one {Entries are kept # day.} \
other {Entries are kept # days.}}`,
            { days: Math.max(result.retentionDays, 0) },
        );
        const folder = result.filesDirectory;
        const files = !result.writeFiles
            ? null
            : folder
              ? $localize`A copy goes to a file per day in ${folder}:folder:.`
              : $localize`A copy goes to a file per day.`;
        const name = this.config.tenant()?.name;
        const scope = result.allSpaces
            ? null
            : name
              ? $localize`This page shows the entries of ${name}:space:.`
              : $localize`This page shows the entries of this space.`;
        return joinParts([kept, files, scope], ' ');
    });

    private typing: ReturnType<typeof setTimeout> | undefined;
    private request: AbortController | null = null;

    constructor() {
        inject(DestroyRef).onDestroy(() => {
            clearTimeout(this.typing);
            this.request?.abort();
        });
        void this.search();
    }

    /** The first page for the filters, replacing what is shown. */
    async search(): Promise<void> {
        clearTimeout(this.typing);
        this.request?.abort();
        const request = (this.request = new AbortController());
        this.loading.set(true);
        this.paging.set(false);
        try {
            const result = await this.api.logs(toQuery(this.filters()), request.signal);
            if (request.signal.aborted) return;
            this.result.set(result);
            this.entries.set(result.entries);
            this.next.set(result.next);
            this.error.set(null);
        } catch (error) {
            if (!request.signal.aborted) this.error.set((error as Error).message);
        } finally {
            if (this.request === request) {
                this.loading.set(false);
                this.loaded.set(true);
                this.request = null;
            }
        }
    }

    protected async loadMore(): Promise<void> {
        const before = this.next();
        if (before === null || this.loading()) return;
        try {
            this.loading.set(true);
            const page = await whileBusy(this.paging, () => this.api.logs(toQuery(this.filters(), before)));
            this.entries.update((entries) => [...entries, ...page.entries]);
            this.next.set(page.next);
        } catch (error) {
            this.error.set((error as Error).message);
        } finally {
            this.loading.set(false);
        }
    }

    protected setText(text: string): void {
        this.filters.update((filters) => ({ ...filters, text }));
        clearTimeout(this.typing);
        this.typing = setTimeout(() => void this.search(), TYPING_MS);
    }

    protected toggleLevel(level: LogLevelName): void {
        this.filters.update((filters) => {
            const levels = new Set(filters.levels);
            if (!levels.delete(level)) levels.add(level);
            return { ...filters, levels };
        });
        void this.search();
    }

    protected setDay(which: 'fromDay' | 'toDay', day: string): void {
        this.filters.update((filters) => ({ ...filters, [which]: day }));
        void this.search();
    }

    protected setSpace(value: string): void {
        const space: SpaceFilter = value === 'all' || value === 'server' ? value : Number(value);
        this.filters.update((filters) => ({ ...filters, space }));
        void this.search();
    }

    protected clearFilters(): void {
        this.filters.set(NO_FILTERS);
        void this.search();
    }

    protected toggle(entry: LogEntry): void {
        this.openId.update((id) => (id === entry.id ? null : entry.id));
    }

    protected levelTitle(level: LogLevelName): string {
        return $localize`Show the entries of severity “${LEVEL_NAMES[level]}:level:” (none ticked: every severity)`;
    }

    protected spaceName(tenantId: number | null): string {
        if (tenantId === null) return $localize`:where a log entry comes from:Server`;
        return this.spaces().find((space) => space.id === tenantId)?.name ?? $localize`Space ${tenantId}:id:`;
    }

    protected time(value: string): string {
        return timeFormat.format(new Date(value));
    }

    protected valueOf(event: Event): string {
        return (event.target as HTMLInputElement | HTMLSelectElement).value;
    }
}
