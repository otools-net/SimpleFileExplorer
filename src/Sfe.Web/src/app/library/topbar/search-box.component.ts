import {
    ChangeDetectionStrategy,
    Component,
    computed,
    effect,
    ElementRef,
    inject,
    signal,
    untracked,
    viewChild,
} from '@angular/core';
import { Api } from '../../core/api.service';
import { plural } from '../../core/format';
import { TagDetails } from '../../core/tag.models';
import { ContextMenu, ContextMenuDirective, MenuTarget } from '../../menus/context-menu';
import { IconComponent } from '../../shared/icon.component';
import { TagChipComponent } from '../../tags/tag-chip.component';
import { describeTag, TAG_TERM, termAt, termValue } from '../../tags/tags';
import { routeParams } from '../library-route';
import { ListingStore } from '../listing.store';
import { SearchBoxState } from '../search-explain/search-explain.component';
import { matchedParts, SearchHistory } from '../search-history.service';
import { SearchHelpDialog } from './search-help.dialog';
import { scopeText, scopeTitle } from './search-scope';
import { SearchScopeMenu } from './search-scope-menu';

const LIVE_SEARCH_MIN_LENGTH = 4; // search as you type once the query is longer than 3 characters
const LIVE_SEARCH_DELAY = 300; // ms of typing pause before the search runs
const LIST_ID = 'search-suggestions';

interface TagTerm {
    start: number;
    end: number;
    negation: string;
    prefix: string;
    text: string;
}

/**
 * The search box: searches as you type (after a pause, once the query is long enough), in the folder browsed.
 * While a `t:` term is typed it suggests tags; a half-typed tag name matches nothing, so the live search waits until
 * a tag is picked or the caret leaves the term. Elsewhere it suggests the searches made before (`SearchHistory`),
 * those starting with the text first; a search is kept when submitted, picked, or when the box is left on its results.
 */
@Component({
    selector: 'form[sfeSearchBox]',
    imports: [IconComponent, SearchHelpDialog, TagChipComponent, ContextMenuDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'search', role: 'search', autocomplete: 'off', '(submit)': 'submit($event)' },
    templateUrl: './search-box.component.html',
})
export class SearchBoxComponent {
    protected readonly store = inject(ListingStore);
    protected readonly search = inject(SearchBoxState);
    private readonly api = inject(Api);
    private readonly history = inject(SearchHistory);
    private readonly scopeMenu = inject(SearchScopeMenu);
    private readonly menu = inject(ContextMenu);
    private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

    protected readonly listId = LIST_ID;
    protected readonly options = signal<TagDetails[]>([]);
    /** Searches made before, suggested while no tag term is typed. */
    protected readonly recent = signal<string[]>([]);
    /** The text the searches were suggested for, shown in bold in them. */
    private readonly recentFor = signal('');
    protected readonly active = signal(-1);
    protected readonly suggesting = signal(false);
    protected readonly emptyText = signal('');
    protected readonly scopeText = computed(() => scopeText(this.store.route(), this.scopeMenu.scopes()));
    protected readonly scopeTitle = computed(() => scopeTitle(this.store.route(), this.scopeMenu.scopes()));
    protected readonly describe = describeTag;
    /** How many files the search found, once its results are listed and only while the box still holds its query. */
    protected readonly found = computed(() => {
        const q = this.store.route().q;
        if (!q || this.store.everywhere() || this.store.status() !== 'ready' || this.search.text().trim() !== q)
            return null;
        const total = this.store.total();
        const count = total?.count ?? this.store.items().length;
        const more = total ? total.capped : this.store.hasMore();
        return {
            text: `${count.toLocaleString()}${more ? '+' : ''}`,
            title: `${plural(count, 'file')}${more ? ' or more' : ''} found`,
        };
    });

    protected parts(query: string) {
        return matchedParts(query, this.recentFor());
    }

    private liveSearchTimer = 0;
    private suggestTimer = 0;
    private sequence = 0;

    constructor() {
        // While typing, keep the box as is (e.g. a trailing space) when it already matches the route.
        effect(() => {
            const q = this.store.route().q;
            untracked(() => {
                const input = this.input().nativeElement;
                if (document.activeElement !== input || input.value.trim() !== q) input.value = q;
                this.search.text.set(input.value);
            });
        });
    }

    focus(): void {
        const input = this.input().nativeElement;
        input.focus();
        input.select();
    }

    /** Adds `term` (an example of the syntax help) at the end of the box and searches as if it had been typed. */
    protected addTerm(term: string): void {
        const input = this.input().nativeElement;
        const text = input.value.trimEnd();
        input.value = text ? `${text} ${term}` : term;
        input.focus();
        input.setSelectionRange(input.value.length, input.value.length);
        this.onInput();
    }

    protected submit(event: Event): void {
        event.preventDefault();
        clearTimeout(this.liveSearchTimer);
        const input = this.input().nativeElement;
        input.blur();
        this.searchFor(input.value.trim());
        this.history.record(input.value);
    }

    /**
     * Lists the results of `q`, unless they are listed already: Enter after a live search (or a phone keyboard's
     * search key, to close the keyboard) would load them again and lose the scroll position.
     */
    private searchFor(q: string): void {
        if (q !== this.store.route().q || this.store.status() === 'error') this.store.navigate({ q });
    }

    protected onInput(): void {
        this.search.text.set(this.input().nativeElement.value);
        clearTimeout(this.liveSearchTimer);
        this.liveSearchTimer = setTimeout(() => {
            if (!this.tagTerm()) this.liveSearch();
        }, LIVE_SEARCH_DELAY);
        clearTimeout(this.suggestTimer);
        this.suggestTimer = setTimeout(() => this.updateSuggestions(), 120);
    }

    /** The native clear button ("x") empties the box: go back to browsing. */
    protected onClear(): void {
        if (!this.input().nativeElement.value && this.store.route().q) this.store.navigate({ q: '' });
    }

    /** While searching: the menu of where to search (this folder, the whole location, everywhere). */
    protected onScope(button: HTMLElement): void {
        if (!this.store.route().q) {
            this.input().nativeElement.focus();
            return;
        }
        this.menu.toggle(this.scopeMenu.target(), button);
    }

    /** `complete`: the query is not still being typed, search it whatever its length. */
    private liveSearch(complete = false): void {
        const q = this.input().nativeElement.value.trim();
        const route = this.store.route();
        if (q === route.q || (q && q.length < LIVE_SEARCH_MIN_LENGTH && !complete)) return;
        // The first keystroke search gets its own history entry; refinements replace it.
        this.store.navigate({ q }, { replace: Boolean(route.q) });
    }

    /* ---------- Tag suggestions ---------- */

    /** The tag term (`t:`, `tag:`, `-t:`...) around the caret, if any. */
    private tagTerm(): TagTerm | null {
        const input = this.input().nativeElement;
        if (input.selectionStart !== input.selectionEnd || input.selectionStart === null) return null;
        const caret = input.selectionStart;
        const { start, end } = termAt(input.value, caret);
        const match = TAG_TERM.exec(input.value.slice(start, caret));
        return match
            ? { start, end, negation: match[1], prefix: match[2], text: match[3].replaceAll('"', '').trim() }
            : null;
    }

    protected async updateSuggestions(): Promise<void> {
        const term = this.tagTerm();
        const request = ++this.sequence;
        if (!term) {
            this.showRecent();
            return;
        }
        this.recent.set([]);
        let results;
        try {
            results = await this.api.tags({ q: term.text, limit: 12 });
        } catch {
            this.hideSuggestions();
            return;
        }
        if (request !== this.sequence || document.activeElement !== this.input().nativeElement) return;
        const options = [...results.matches, ...results.descendants].slice(0, 12);
        this.options.set(options);
        this.active.set(options.length ? 0 : -1);
        this.emptyText.set(term.text ? `No tag matches “${term.text}”.` : 'No tags yet.');
        this.suggesting.set(true);
    }

    protected hideSuggestions(): void {
        this.sequence++;
        this.options.set([]);
        this.recent.set([]);
        this.active.set(-1);
        this.suggesting.set(false);
    }

    /** Leaving the box on the results of what it holds (searched as typed) keeps that search. */
    protected onBlur(): void {
        this.hideSuggestions();
        const q = this.store.route().q;
        if (q && q === this.input().nativeElement.value.trim()) this.history.record(q);
    }

    /* ---------- Search history ---------- */

    /** None is highlighted at first, so Enter still searches what was typed. */
    private showRecent(): void {
        const input = this.input().nativeElement;
        const recent = document.activeElement === input ? this.history.suggest(input.value) : [];
        this.options.set([]);
        this.recent.set(recent);
        this.recentFor.set(input.value);
        this.active.set(-1);
        this.suggesting.set(recent.length > 0);
    }

    protected pickRecent(query: string): void {
        const input = this.input().nativeElement;
        input.value = query;
        this.search.text.set(query);
        clearTimeout(this.liveSearchTimer);
        clearTimeout(this.suggestTimer);
        input.blur();
        this.searchFor(query);
        this.history.record(query);
    }

    /** The menu of a search of the history: search it again (here, or in a new tab), or forget it. */
    protected recentTarget(query: string): MenuTarget {
        return {
            kind: 'historyEntry',
            key: `history:${query}`,
            name: query,
            entries: () => [
                {
                    id: 'search',
                    label: 'Search',
                    icon: 'search',
                    title: 'Search this again',
                    run: () => this.pickRecent(query),
                },
                {
                    id: 'newTab',
                    label: 'Open in new tab',
                    icon: 'external',
                    title: 'This search in a tab of its own',
                    newTab: true,
                    href: this.store.pageUrl(routeParams({ ...this.store.route(), q: query })),
                },
                {
                    id: 'remove',
                    label: 'Remove from the history',
                    icon: 'close',
                    title: 'Forget this search',
                    run: () => this.history.remove(query),
                },
            ],
        };
    }

    protected removeRecent(event: Event, query: string): void {
        event.stopPropagation();
        this.history.remove(query);
        this.showRecent();
    }

    protected pick(tag: TagDetails): void {
        const term = this.tagTerm();
        if (!term) return;
        const input = this.input().nativeElement;
        const value = input.value;
        const completed = term.negation + term.prefix + termValue(tag.name) + (term.end === value.length ? ' ' : '');
        input.value = value.slice(0, term.start) + completed + value.slice(term.end);
        const caret = term.start + completed.length;
        input.setSelectionRange(caret, caret);
        this.search.text.set(input.value);
        this.hideSuggestions();
        clearTimeout(this.liveSearchTimer);
        this.liveSearch(true);
    }

    /** Moving the caret into or out of a tag term. */
    protected onKeyup(event: KeyboardEvent): void {
        if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) void this.updateSuggestions();
    }

    protected onKeydown(event: KeyboardEvent): void {
        if (!this.suggesting()) return;
        const recent = this.recent();
        if (recent.length) {
            this.onHistoryKeydown(event, recent);
            return;
        }
        const options = this.options();
        if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && options.length) {
            event.preventDefault();
            this.active.update(
                (active) => (active + (event.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length,
            );
            this.scrollActiveIntoView();
        } else if ((event.key === 'Enter' || event.key === 'Tab') && this.active() >= 0) {
            // Complete the term instead of submitting the search or leaving the box.
            event.preventDefault();
            this.pick(options[this.active()]);
        } else if (event.key === 'Escape') {
            // Close the suggestions; a search box would also clear itself on Escape.
            event.preventDefault();
            this.hideSuggestions();
        }
    }

    /** The arrows go round the searches and back to none (-1); Enter searches the highlighted one, else the box. */
    private onHistoryKeydown(event: KeyboardEvent, recent: string[]): void {
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            const count = recent.length + 1;
            this.active.update((active) => ((active + 1 + (event.key === 'ArrowDown' ? 1 : -1) + count) % count) - 1);
            this.scrollActiveIntoView();
        } else if (event.key === 'Enter' && this.active() >= 0) {
            event.preventDefault();
            this.pickRecent(recent[this.active()]);
        } else if (event.key === 'Escape') {
            event.preventDefault();
            this.hideSuggestions();
        }
    }

    private scrollActiveIntoView(): void {
        queueMicrotask(() =>
            document.getElementById(`${LIST_ID}-${this.active()}`)?.scrollIntoView({ block: 'nearest' }),
        );
    }
}
