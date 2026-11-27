import {
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    effect,
    ElementRef,
    inject,
    signal,
    untracked,
    viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api, API_BASE, ApiError } from '../core/api.service';
import { renderMarkdown, highlightFences } from '../formats/markdown/markdown';
import { ReadingService } from '../formats/reading/reading.service';
import { IconComponent } from '../shared/icon.component';
import { HELP_HOME, helpLink, helpMarkdownLinks } from './help-links';
import { HelpMatch, helpGroups, searchHelp } from './help-search';
import { HelpPage } from './help.models';

type HelpState = 'loading' | 'ready' | 'missing' | 'signed-out' | 'unreachable';

/**
 * `help?page=…#section`: the user documentation shipped with the app (agents/docs/features/help.md), a page rendered
 * as the Markdown viewer renders a document, its links between pages kept in the help, its pictures from the help's
 * files. The contents beside it (a panel on phones), a search over the titles and headings of every page.
 */
@Component({
    selector: 'sfe-help-page',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'help-page' },
    templateUrl: './help-page.component.html',
})
export class HelpPageComponent {
    private readonly api = inject(Api);
    private readonly router = inject(Router);
    private readonly title = inject(Title);
    private readonly reading = inject(ReadingService);
    private readonly route = inject(ActivatedRoute);
    private readonly query = toSignal(this.route.queryParamMap);
    private readonly fragment = toSignal(this.route.fragment);
    private readonly links = helpMarkdownLinks(API_BASE);
    private readonly content = viewChild<ElementRef<HTMLElement>>('content');
    private loading: AbortController | null = null;

    protected readonly page = computed(() => this.query()?.get('page') || HELP_HOME);
    protected readonly pages = signal<HelpPage[]>([]);
    protected readonly state = signal<HelpState>('loading');
    protected readonly text = signal<string | null>(null);
    protected readonly search = signal('');
    protected readonly contentsOpen = signal(false);
    protected readonly groups = computed(() => helpGroups(this.pages()));
    protected readonly matches = computed(() => searchHelp(this.pages(), this.search()));
    /** The prose reading preferences; no width set: one easy to read. */
    protected readonly style = computed(() => {
        const style = this.reading.style('prose');
        return style['--reader-width'] === 'none' ? { ...style, '--reader-width': '860px' } : style;
    });
    /** Where the sign-in page comes back to: this page of the help. */
    protected readonly signInReturn = computed(() => `/${helpLink(this.page())}`);

    constructor() {
        void this.loadIndex();
        effect(() => {
            const page = this.page();
            untracked(() => void this.loadPage(page));
        });
        effect(() => {
            const element = this.content()?.nativeElement;
            const text = this.text();
            if (element && text !== null) untracked(() => this.show(element, text));
        });
        effect(() => {
            const section = this.fragment();
            if (this.state() === 'ready') untracked(() => this.scrollTo(section ?? null));
        });
        inject(DestroyRef).onDestroy(() => this.loading?.abort());
    }

    /** The query of a page's address (none for the first page), for `routerLink`. */
    protected pageQuery(page: string): Record<string, string> | null {
        return page === HELP_HOME ? null : { page };
    }

    protected chose(match: HelpMatch | null): void {
        this.search.set('');
        this.contentsOpen.set(false);
        if (match) this.scrollTo(match.section);
    }

    /** Links between pages and to headings stay in the page (a modified click opens a tab, as links do). */
    protected follow(event: MouseEvent): void {
        const anchor = (event.target as Element | null)?.closest('a');
        if (!anchor || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const path = anchor.dataset['path'];
        const section = anchor.dataset['anchor']?.replace(/^md-/, '') ?? null;
        if (path === undefined && section === null) return;
        event.preventDefault();
        const fragment = path === undefined ? section : anchor.hash.slice(1) || undefined;
        void this.router.navigate(['/help'], {
            queryParams: this.pageQuery(path ?? this.page()),
            fragment: fragment ?? undefined,
        });
    }

    private async loadIndex(): Promise<void> {
        try {
            this.pages.set((await this.api.helpIndex()).pages);
        } catch (error) {
            this.failed(error);
        }
    }

    private async loadPage(page: string): Promise<void> {
        this.loading?.abort();
        const loading = (this.loading = new AbortController());
        this.state.set('loading');
        try {
            const text = await this.api.helpPage(page, loading.signal);
            this.text.set(text);
            this.state.set('ready');
        } catch (error) {
            if (!loading.signal.aborted) this.failed(error);
        }
    }

    private failed(error: unknown): void {
        const status = error instanceof ApiError ? error.status : 0;
        this.state.set(status === 401 ? 'signed-out' : status === 404 ? 'missing' : 'unreachable');
        this.text.set(null);
    }

    private show(element: HTMLElement, text: string): void {
        const page = this.page();
        const rendered = renderMarkdown(text, page, this.links);
        // A heading's link in the page: its address in the help, so a new tab opens it too.
        for (const anchor of rendered.fragment.querySelectorAll<HTMLAnchorElement>('a[data-anchor]'))
            anchor.setAttribute('href', helpLink(page, anchor.dataset['anchor']!.replace(/^md-/, '')));
        element.replaceChildren(rendered.fragment);
        void highlightFences(element);
        const title = rendered.headings.find((heading) => heading.level === 1)?.text;
        this.title.setTitle(title ? $localize`${title}:title: · SFE help` : $localize`SFE help`);
        this.scrollTo(this.fragment() ?? null);
    }

    private scrollTo(section: string | null): void {
        const element = this.content()?.nativeElement;
        if (!element) return;
        const target = section ? element.querySelector(`#md-${CSS.escape(section)}`) : null;
        if (target) target.scrollIntoView({ block: 'start' });
        else {
            const main = element.closest('.help-main');
            if (main) main.scrollTop = 0;
        }
    }
}
