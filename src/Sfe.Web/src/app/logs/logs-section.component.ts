import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, output, signal } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { plural } from '../core/format';
import { LogEntry, LogLevelName, LogSearchResult } from '../core/models';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { hasFilters, LEVEL_NAMES, LOG_LEVELS, LogFilters, NO_FILTERS, shortCategory, SpaceFilter, toQuery } from './logs';

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
    template: `
        <form class="logs-filters" role="search" aria-label="Search the log" (submit)="$event.preventDefault(); search()">
            <div class="logs-filter-row">
                <label class="logs-text">
                    <span class="visually-hidden">Text</span>
                    <svg sfeIcon="search"></svg>
                    <input type="search" placeholder="Search messages, loggers and errors" title="Text found in the message, the logger's name or the error (ignoring case)"
                           [value]="filters().text" (input)="setText(valueOf($event))">
                </label>
                <button type="submit" class="button small" title="Read the log again" [sfeBusy]="loading() && !paging()"><svg sfeIcon="refresh"></svg>Refresh</button>
            </div>
            <div class="logs-filter-row">
                <fieldset class="logs-levels">
                    <legend class="visually-hidden">Severity</legend>
                    @for (level of levels; track level) {
                        <label class="logs-level-chip" [class]="'level-' + level" [title]="'Show ' + levelNames[level] + ' entries (none ticked: every severity)'">
                            <input type="checkbox" [checked]="filters().levels.has(level)" (change)="toggleLevel(level)"><span>{{ levelNames[level] }}</span>
                        </label>
                    }
                </fieldset>
                <label class="logs-day">
                    <span>From</span>
                    <input type="date" title="Entries from this day on" [value]="filters().fromDay" (change)="setDay('fromDay', valueOf($event))">
                </label>
                <label class="logs-day">
                    <span>To</span>
                    <input type="date" title="Entries up to the end of this day" [value]="filters().toDay" (change)="setDay('toDay', valueOf($event))">
                </label>
                @if (allSpaces()) {
                    <label class="logs-space">
                        <span class="visually-hidden">Space</span>
                        <select title="Show the entries of one space, or the server's own" (change)="setSpace(valueOf($event))">
                            <option value="all" [selected]="filters().space === 'all'">All spaces and the server</option>
                            <option value="server" [selected]="filters().space === 'server'">The server only</option>
                            @for (space of spaces(); track space.id) {
                                <option [value]="space.id" [selected]="filters().space === space.id">{{ space.name }}</option>
                            }
                        </select>
                    </label>
                }
                @if (filtered()) {
                    <button type="button" class="button small" title="Show every entry again" (click)="clearFilters()">Clear filters</button>
                }
            </div>
        </form>

        <div class="logs-bar">
            <span class="field-hint">{{ keptText() }}</span>
            <span class="spacer"></span>
            <button type="button" class="button small" title="Change how long entries are kept, and whether they are saved to files" (click)="openSettings.emit()">
                <svg sfeIcon="settings"></svg>Keep for…
            </button>
        </div>

        @if (error()) {
            <p class="form-error">Could not read the log: {{ error() }}</p>
        }
        <ul class="log-entries" aria-live="polite">
            @for (entry of entries(); track entry.id) {
                @let open = openId() === entry.id;
                <li class="log-entry" [class]="'level-' + entry.level" [class.open]="open">
                    <button type="button" class="log-entry-head" [attr.aria-expanded]="open"
                            [title]="open ? 'Show less' : 'Show the whole entry'" (click)="toggle(entry)">
                        <time [attr.datetime]="entry.timestamp">{{ time(entry.timestamp) }}</time>
                        <span class="log-level">{{ levelNames[entry.level] }}</span>
                        @if (allSpaces()) {
                            <span class="log-space">{{ spaceName(entry.tenantId) }}</span>
                        }
                        <span class="log-category" [title]="entry.category">{{ short(entry.category) }}</span>
                        <span class="log-message">{{ entry.message }}</span>
                        @if (entry.exception && !open) {
                            <span class="log-has-error" title="An error is attached">error</span>
                        }
                    </button>
                    @if (open) {
                        <div class="log-entry-details">
                            <p><span class="muted">Logger</span> {{ entry.category }}</p>
                            @if (entry.exception) {
                                <pre class="log-exception">{{ entry.exception }}</pre>
                            }
                        </div>
                    }
                </li>
            }
        </ul>
        @if (!entries().length && loaded() && !error()) {
            <p class="muted task-empty">{{ filtered() ? 'No entries match these filters.' : 'Nothing logged yet.' }}</p>
        }
        @if (next() !== null) {
            <button type="button" class="button small task-more" title="Show older entries" [sfeBusy]="paging()" [disabled]="loading()" (click)="loadMore()">
                Load older entries
            </button>
        }
    `,
})
export class LogsSectionComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);

    readonly openSettings = output<void>();

    protected readonly levels = LOG_LEVELS;
    protected readonly levelNames = LEVEL_NAMES;
    protected readonly short = shortCategory;

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
        if (!result)
            return '';
        const kept = result.retentionDays > 0 ? `Entries are kept ${plural(result.retentionDays, 'day')}` : 'Entries are kept forever';
        const files = result.writeFiles ? `; a copy goes to a file per day${result.filesDirectory ? ` in ${result.filesDirectory}` : ''}` : '';
        const scope = result.allSpaces ? '' : ` This page shows the entries of ${this.config.tenant()?.name ?? 'this space'}.`;
        return `${kept}${files}.${scope}`;
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
        const request = this.request = new AbortController();
        this.loading.set(true);
        this.paging.set(false);
        try {
            const result = await this.api.logs(toQuery(this.filters()), request.signal);
            if (request.signal.aborted)
                return;
            this.result.set(result);
            this.entries.set(result.entries);
            this.next.set(result.next);
            this.error.set(null);
        } catch (error) {
            if (!request.signal.aborted)
                this.error.set((error as Error).message);
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
        if (before === null || this.loading())
            return;
        try {
            this.loading.set(true);
            const page = await whileBusy(this.paging, () => this.api.logs(toQuery(this.filters(), before)));
            this.entries.update(entries => [...entries, ...page.entries]);
            this.next.set(page.next);
        } catch (error) {
            this.error.set((error as Error).message);
        } finally {
            this.loading.set(false);
        }
    }

    protected setText(text: string): void {
        this.filters.update(filters => ({ ...filters, text }));
        clearTimeout(this.typing);
        this.typing = setTimeout(() => void this.search(), TYPING_MS);
    }

    protected toggleLevel(level: LogLevelName): void {
        this.filters.update(filters => {
            const levels = new Set(filters.levels);
            if (!levels.delete(level))
                levels.add(level);
            return { ...filters, levels };
        });
        void this.search();
    }

    protected setDay(which: 'fromDay' | 'toDay', day: string): void {
        this.filters.update(filters => ({ ...filters, [which]: day }));
        void this.search();
    }

    protected setSpace(value: string): void {
        const space: SpaceFilter = value === 'all' || value === 'server' ? value : Number(value);
        this.filters.update(filters => ({ ...filters, space }));
        void this.search();
    }

    protected clearFilters(): void {
        this.filters.set(NO_FILTERS);
        void this.search();
    }

    protected toggle(entry: LogEntry): void {
        this.openId.update(id => id === entry.id ? null : entry.id);
    }

    protected spaceName(tenantId: number | null): string {
        if (tenantId === null)
            return 'Server';
        return this.spaces().find(space => space.id === tenantId)?.name ?? `Space ${tenantId}`;
    }

    protected time(value: string): string {
        return timeFormat.format(new Date(value));
    }

    protected valueOf(event: Event): string {
        return (event.target as HTMLInputElement | HTMLSelectElement).value;
    }
}
