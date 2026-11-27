import { Location, NgTemplateOutlet } from '@angular/common';
import {
    afterNextRender,
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    Directive,
    effect,
    ElementRef,
    inject,
    Injector,
    input,
    OnInit,
    signal,
    untracked,
    viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { icu } from '../core/i18n/icu';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { redirected } from '../core/api.service';
import { SettingLevel } from '../core/setting.models';
import { ConfigService } from '../core/config.service';
import { ThemeService } from '../core/theme.service';
import { routeParams } from '../library/library-route';
import { ListingStore } from '../library/listing.store';
import { LogsSectionComponent } from '../logs/logs-section.component';
import { SettingsForm } from '../settings/settings-form.service';
import { settingFieldId } from '../settings/settings-form.service';
import { SettingsPanelComponent } from '../settings/settings-panel.component';
import { SIMPLE_SETTINGS, TABS } from '../settings/settings-schema';
import { valueOf } from '../shared/events';
import { EmptyStateComponent } from '../shared/empty-state.component';
import { ContextMenu, usePageMenu } from '../menus/context-menu';
import { IconComponent } from '../shared/icon.component';
import { TagEditorDialog } from '../tags/tag-editor.dialog';
import { TagListComponent } from '../tags/tag-list.component';
import { TasksSectionComponent } from '../tasks/tasks-section.component';
import { TrackingSectionComponent } from '../tracking/tracking-section.component';
import { TrickplayActivityComponent } from '../trickplay/trickplay-activity.component';
import { ConnectionsSectionComponent } from '../connections/connections-section.component';
import { KeepAliveStatusComponent } from './keep-alive-status.component';
import { AccountSectionComponent } from '../account/account-section.component';
import { AccessSectionComponent } from '../access/access-section.component';
import { ADMIN_SEARCH_LIMIT, AdminSearchResult, markWords, searchAdmin, searchWords } from './admin-search';
import {
    ADMIN_SECTIONS,
    AdminSection,
    AdminSubpage,
    findSection,
    findSubpage,
    ADMIN_GROUP_TITLES,
    groupId,
    groupsOf,
    locateSettings,
    movedPage,
    visibleSections,
} from './admin-sections';
import { SystemSectionComponent } from './system-section.component';
import { TrashSectionComponent } from '../files/trash-section.component';
import { UploadsSectionComponent } from '../files/uploads-section.component';
import { VideoStreamsComponent } from './video-streams.component';
import { NotificationsComponent } from '../notifications/notifications.component';
import { ExclusionsSectionComponent } from '../library/scan/exclusions-section.component';
import { adminHelpLink } from '../help/admin-help';
import { canOpenWizard, WIZARDS } from '../wizards/wizard-registry';

/** History state of the entries of this page opened from the listing page: "back" returns there. */
const FROM_LIBRARY = 'sfeFromLibrary';
/** History state of a section opened from the list of sections (phones): "back" returns to the list. */
const FROM_LIST = 'sfeFromList';
/** Phones: the sections are a list of their own instead of a sidebar (the breakpoint of admin.css). */
const PHONE = '(max-width: 760px)';

/** The level tabs' names, in the save bar. */
const LEVEL_TITLES: Partial<Record<SettingLevel, string>> = {
    tenant: $localize`:settings level:This space`,
    server: $localize`:settings level:Whole server`,
};

export { leaveAdminPage } from './leave-admin-page';

/**
 * The administration page (`admin?section=&page=`): the sections of admin-sections.ts in a sidebar, the sub-pages of
 * the open section listed under it (on phones, where the sections are a list of their own, as chips above the
 * sub-page); breadcrumbs above the heading say where the page is. Without a section, it shows the sections as cards by
 * group (the overview; on phones, the list of sections), unless the session sees only one. A search box above the
 * sections finds sections, sub-pages and settings (admin-search.ts). The settings sub-pages share one form
 * (SettingsForm), saved from the bar at the bottom. Moving between sections and sub-pages replaces the history entry,
 * so that "back" returns to the listing page.
 */
/**
 * Phones: the sub-pages' chips are one line that scrolls sideways; the one shown (`sfeChipInView`) is brought into
 * view.
 */
@Directive({ selector: '[sfeChipInView]' })
export class ChipInViewDirective {
    readonly sfeChipInView = input.required<string>();

    constructor() {
        const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
        const injector = inject(Injector);
        effect(() => {
            this.sfeChipInView();
            // Drawn with the strip (its section loaded), and again when another sub-page is chosen.
            afterNextRender(() => showChip(host.querySelector<HTMLElement>('[aria-selected="true"]')), { injector });
        });
    }
}

@Component({
    selector: 'sfe-admin-page',
    imports: [
        ChipInViewDirective,
        RouterLink,
        NgTemplateOutlet,
        IconComponent,
        EmptyStateComponent,
        SettingsPanelComponent,
        TagListComponent,
        TagEditorDialog,
        TrackingSectionComponent,
        TasksSectionComponent,
        TrickplayActivityComponent,
        KeepAliveStatusComponent,
        SystemSectionComponent,
        AccessSectionComponent,
        AccountSectionComponent,
        ConnectionsSectionComponent,
        LogsSectionComponent,
        VideoStreamsComponent,
        TrashSectionComponent,
        UploadsSectionComponent,
        ExclusionsSectionComponent,
        NotificationsComponent,
    ],
    providers: [SettingsForm],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'admin-page', '(window:beforeunload)': 'onBeforeUnload($event)' },
    templateUrl: './admin-page.component.html',
})
export class AdminPageComponent implements OnInit {
    protected readonly form = inject(SettingsForm);
    protected readonly config = inject(ConfigService);
    private readonly theme = inject(ThemeService);
    private readonly store = inject(ListingStore);
    private readonly router = inject(Router);
    private readonly injector = inject(Injector);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    private readonly tracking = viewChild(TrackingSectionComponent);
    private readonly tasks = viewChild(TasksSectionComponent);
    private readonly tagList = viewChild(TagListComponent);

    /** The sections and sub-pages the session has rights for (all of them until the configuration is loaded). */
    private readonly sections = computed(() => {
        const visible = visibleSections((rights) => this.config.canAny(rights));
        return visible.length ? visible : ADMIN_SECTIONS;
    });
    protected readonly groups = computed(() => groupsOf(this.sections()));
    /** Nothing here for this session (a visitor without an account). */
    protected readonly empty = computed(() => visibleSections((rights) => this.config.canAny(rights)).length === 0);
    protected readonly ready = signal(false);
    protected readonly unreachable = signal<string | null>(null);

    private readonly route = inject(ActivatedRoute);
    private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });
    /** The group the overview scrolls to (the breadcrumbs' group link). */
    private readonly fragment = toSignal(this.route.fragment, { requireSync: true });
    protected readonly section = computed(() => findSection(this.params().get('section'), this.sections()));
    protected readonly subpage = computed(() => findSubpage(this.section(), this.params().get('page')));
    /** The help about the sub-page shown (the "?" by its title). */
    protected readonly helpLink = computed(() => adminHelpLink(this.section().id, this.subpage().id) ?? 'help');

    private readonly location = inject(Location);
    /** Opened from the listing page: kept in the entries this page replaces, so that "back" still returns there. */
    private readonly fromLibrary = Boolean(
        (this.location.getState() as Record<string, unknown> | null)?.[FROM_LIBRARY],
    );
    protected readonly historyState = { [FROM_LIBRARY]: this.fromLibrary };

    /**
     * On phones there is no room for the sidebar: the page without a section is the list of sections, and each section
     * is a page of its own, opened from it as a new history entry so that "back" (the browser's too) returns to it.
     */
    protected readonly phone = signal(false);
    protected readonly sectionList = computed(() => this.phone() && !this.params().has('section'));
    /** Computers, without a section: the sections as cards, by group (a session seeing one section gets it instead). */
    protected readonly overview = computed(
        () => !this.phone() && !this.params().has('section') && this.sections().length > 1,
    );
    protected readonly groupId = groupId;
    protected readonly linkState = computed(() =>
        this.sectionList() ? { ...this.historyState, [FROM_LIST]: true } : this.historyState,
    );
    protected readonly backHint = computed(() =>
        this.phone() && !this.sectionList() ? $localize`Back to the sections` : $localize`Back to the library`,
    );
    /** The settings tab of settings-schema.ts on screen, if any. */
    protected readonly settingsShown = computed(() =>
        this.sectionList() || this.overview() ? undefined : this.subpage().settings,
    );

    /** The search box's text, and what it finds among the sections the session sees. */
    protected readonly query = signal('');
    private readonly words = computed(() => searchWords(this.query()));
    protected readonly searching = computed(() => this.words().length > 0);
    private readonly wizards = computed(() =>
        WIZARDS.filter((w) => canOpenWizard(w, (right) => this.config.can(right), this.config.config()?.mode)),
    );
    private readonly results = computed(() => searchAdmin(this.query(), this.sections(), this.wizards()));
    protected readonly shownResults = computed(() => this.results().slice(0, ADMIN_SEARCH_LIMIT));
    protected readonly resultsCount = computed(() => {
        const count = this.results().length;
        return count > ADMIN_SEARCH_LIMIT
            ? icu($localize`The first {limit, number} of {count, number} found: add words to narrow them down.`, {
                  limit: ADMIN_SEARCH_LIMIT,
                  count,
              })
            : icu($localize`{count, plural, =0 {Nothing found} one {# found} other {# found}}`, { count });
    });
    /** The setting picked among the results, brought into view once its sub-page shows it. */
    private readonly reveal = signal<string | null>(null);
    protected readonly valueOf = valueOf;
    protected readonly openPlace = (where: string) => $localize`Open ${where}:place:`;
    protected readonly openAtSetting = (where: string) => $localize`Open ${where}:place: at this setting`;

    protected readonly saveStatus = computed(() => {
        const changed = [...this.form.changedTabs()].map((id) => this.titleOfSettings(id)).filter(Boolean);
        const others = [...this.form.changedLevels()]
            .filter((level) => level !== this.form.level())
            .map((level) => LEVEL_TITLES[level]);
        if (changed.length)
            return others.length
                ? $localize`Not saved yet: ${changed.join(', ')}:pages:; also in ${others.join(', ')}:levels:`
                : $localize`Not saved yet: ${changed.join(', ')}:pages:`;
        if (others.length) return $localize`Not saved yet in ${others.join(', ')}:levels:`;
        // Servers without levels answer without states: their settings are the server's.
        if (this.form.states() && this.form.level() === 'tenant')
            return this.form.customized()
                ? $localize`This space has settings of its own`
                : $localize`Following the server's settings`;
        return this.form.customized() ? $localize`Using saved settings` : $localize`Using the server's configuration`;
    });
    protected readonly groupTitle = computed(() => ADMIN_GROUP_TITLES[this.section().group]);
    protected readonly discardHint = computed(() =>
        this.form.levels().length > 1
            ? $localize`Undo the changes not saved yet, on every page of both tabs`
            : $localize`Undo the changes not saved yet, on every page`,
    );
    protected readonly resetHint = computed(() =>
        !this.form.customized()
            ? $localize`Nothing saved yet`
            : this.form.states() && this.form.level() === 'tenant'
              ? $localize`Forget the space's own settings and use the server's again (asks first)`
              : $localize`Forget the saved settings and go back to the server's configuration (asks first)`,
    );
    protected readonly saveHint = computed(() =>
        this.form.changedLevels().size > 1
            ? $localize`Save and apply the settings changed in both tabs: the whole server's, then this space's`
            : this.form.states() && this.form.level() === 'tenant'
              ? $localize`Save and apply the settings of every page, for everyone using this space`
              : $localize`Save and apply the settings of every page, for everyone using this server`,
    );

    constructor() {
        // A right click on the page (not on a link, a field or a menu of its own): back, reload, its address.
        usePageMenu(inject(ContextMenu).pageTarget($localize`Administration`));
        const title = inject(Title);
        effect(() => {
            if (this.overview() || this.sectionList()) return title.setTitle($localize`Administration – SFE`);
            const section = this.section();
            title.setTitle(
                section.subpages.length > 1
                    ? $localize`${this.subpage().title}:page: – ${section.title}:section: – Administration – SFE`
                    : $localize`${section.title}:section: – Administration – SFE`,
            );
        });
        // The address of a sub-page that moved (a bookmark, history): replaced by where it is now.
        effect(() => {
            const params = this.params();
            const moved = movedPage(params.get('section'), params.get('page'));
            if (moved) untracked(() => void this.go(moved.section, moved.page));
        });
        // The overview opens at its top, or at the group of the breadcrumbs' link.
        effect(() => {
            if (!this.overview()) return;
            const fragment = this.fragment();
            afterNextRender(
                () => {
                    const group = fragment && document.getElementById(fragment);
                    if (group) group.scrollIntoView({ block: 'start' });
                    else window.scrollTo({ top: 0 });
                },
                { injector: this.injector },
            );
        });
        const phoneQuery = matchMedia(PHONE);
        const onPhoneChange = () => this.phone.set(phoneQuery.matches);
        onPhoneChange();
        phoneQuery.addEventListener('change', onPhoneChange);
        inject(DestroyRef).onDestroy(() => phoneQuery.removeEventListener('change', onPhoneChange));
        // On phones a section (or the list) is a page of its own: it opens at its top.
        effect(() => {
            this.section();
            this.sectionList();
            if (untracked(this.phone)) window.scrollTo({ top: 0 });
        });
        // A setting picked in the search: once its sub-page and the settings are shown, it is scrolled to and focused.
        effect(() => {
            const key = this.reveal();
            if (key && this.form.loaded() && this.subpage().settings)
                afterNextRender(() => this.revealSetting(key), { injector: this.injector });
        });
        // The tag list loads once shown (it is created again with its section).
        effect(() => {
            const list = this.tagList();
            untracked(() => void list?.load());
        });
    }

    async ngOnInit(): Promise<void> {
        window.scrollTo({ top: 0 });
        try {
            this.theme.setDefault((this.config.config() ?? (await this.config.load())).theme);
        } catch (error) {
            if (!redirected(error)) this.unreachable.set((error as Error).message);
            return;
        }
        this.ready.set(true);
        // A user who may change no settings (My account only: its own form) asks nothing: the space's were refused
        // (403).
        if (this.form.levels().length) void this.form.load();
    }

    /** False when the user would rather stay, to save the settings changed. */
    canLeave(): boolean {
        return !this.form.changed() || confirm($localize`Leave without saving? The settings you changed are lost.`);
    }

    /** Some settings of `pages` need fixing. */
    protected faulty(pages: AdminSubpage[]): boolean {
        const faulty = this.form.faultyTabs();
        return pages.some((page) => page.settings && faulty.has(page.settings));
    }

    /** Some settings of `pages` are changed and not saved yet. */
    protected changed(pages: AdminSubpage[]): boolean {
        const changed = this.form.changedTabs();
        return pages.some((page) => page.settings && changed.has(page.settings));
    }

    protected badge(page: string): string {
        const badges: Record<string, string | undefined> = this.tracking()?.badges() ?? this.tasks()?.badges() ?? {};
        return badges[page] ?? '';
    }

    protected checked(event: Event): boolean {
        return (event.target as HTMLInputElement).checked;
    }

    protected marked(text: string): { text: string; match: boolean }[] {
        return markWords(text, this.words());
    }

    protected resultParams(result: AdminSearchResult): Record<string, string | null> {
        if (result.wizard) return { id: result.wizard };
        return {
            section: result.section.id,
            page: result.subpage.id === result.section.subpages[0].id ? null : result.subpage.id,
        };
    }

    /** A result is opened: a setting is brought into view, in the full view when the simple one hides it. */
    protected found(result: AdminSearchResult): void {
        if (!result.setting) return;
        // As for an error on a hidden setting: shown without remembering the choice.
        if (!SIMPLE_SETTINGS.has(result.setting)) this.form.showAll.set(true);
        this.reveal.set(result.setting);
    }

    /** Enter opens the first result, Escape empties the box. */
    protected onSearchKey(event: KeyboardEvent): void {
        if (event.key === 'Escape' && this.query()) {
            event.preventDefault();
            this.query.set('');
        } else if (event.key === 'Enter') {
            event.preventDefault();
            const first = this.results()[0];
            if (!first) return;
            this.found(first);
            void this.router.navigate([first.wizard ? '/wizard' : '/admin'], {
                queryParams: this.resultParams(first),
                state: this.linkState(),
                replaceUrl: !this.phone() && !first.wizard,
            });
        }
    }

    /** Scrolls to the field of the setting `key` and focuses it, once its sub-page shows it. */
    private revealSetting(key: string): void {
        const control = this.host.querySelector<HTMLElement>(`#${settingFieldId(key)}`);
        const field = control?.closest<HTMLElement>('.field, .setting-check');
        if (!control || !field || this.reveal() !== key) return;
        this.reveal.set(null);
        field.scrollIntoView({ block: 'center' });
        control.focus({ preventScroll: true });
        // Flashes, again when found again.
        field.classList.remove('setting-found');
        void field.offsetWidth;
        field.classList.add('setting-found');
        field.addEventListener('animationend', () => field.classList.remove('setting-found'), { once: true });
    }

    /** "Section › Sub-page" of the tab `settings` of settings-schema.ts, the section alone when it has one sub-page. */
    private titleOfSettings(settings: string): string | undefined {
        const found = locateSettings(settings);
        return (
            found &&
            (found.section.subpages.length > 1
                ? `${found.section.title} › ${found.subpage.title}`
                : found.section.title)
        );
    }

    /** Shows the sub-page `page` of the current section. */
    protected select(page: string, { focus = false } = {}): void {
        void this.go(this.section().id, page).then(() => {
            if (focus)
                afterNextRender(() => document.getElementById(`admin-chip-${page}`)?.focus(), {
                    injector: this.injector,
                });
        });
    }

    protected go(section: string, page?: string): Promise<boolean> {
        return this.router.navigate(['/admin'], {
            queryParams: this.pageParams(findSection(section, this.sections()), page),
            replaceUrl: true,
            state: this.stayState(),
        });
    }

    /**
     * The address of a sub-page (none for the section's first one). The space a sub-page of Spaces & accounts is about
     * stays from one to the other.
     */
    protected pageParams(section: AdminSection, page?: string) {
        const space = section.id === 'access' && this.section().id === 'access' ? this.params().get('space') : null;
        return { section: section.id, page: page && page !== section.subpages[0].id ? page : null, space };
    }

    /** The history state of an entry replacing this one: it still returns where this one did. */
    protected stayState(): Record<string, boolean> {
        return this.openedFromList() ? { ...this.historyState, [FROM_LIST]: true } : this.historyState;
    }

    /** The entry shown is a section opened from the list of sections (phones). */
    private openedFromList(): boolean {
        return Boolean((this.location.getState() as Record<string, unknown> | null)?.[FROM_LIST]);
    }

    /**
     * Back where the page was opened from (its scroll position and loaded items too), or to the library; on phones,
     * from a section to the list of sections.
     */
    protected back(): void {
        if (this.phone() && !this.sectionList()) {
            // A section on a phone: back to the list of sections, through history when it was opened from there.
            if (this.openedFromList()) history.back();
            else void this.router.navigate(['/admin'], { replaceUrl: true, state: this.historyState });
        } else if (this.fromLibrary) history.back();
        else void this.router.navigate(['/'], { queryParams: routeParams(this.store.route()) });
    }

    /** Arrow keys move between the chips (the WAI-ARIA tabs pattern); Tab goes into the panel. */
    protected onChipKey(event: KeyboardEvent): void {
        const ids = this.section().subpages.map((page) => page.id);
        const index = ids.indexOf(this.subpage().id);
        const target = (
            { ArrowLeft: index - 1, ArrowRight: index + 1, Home: 0, End: ids.length - 1 } as Record<string, number>
        )[event.key];
        if (target === undefined) return;
        event.preventDefault();
        this.select(ids[(target + ids.length) % ids.length], { focus: true });
    }

    /** Arrow keys, Home and End move between the level tabs (this space, whole server), as between the chips. */
    protected onLevelKey(event: KeyboardEvent): void {
        const levels = this.form.levels();
        const index = levels.indexOf(this.form.level());
        const target = (
            { ArrowLeft: index - 1, ArrowRight: index + 1, Home: 0, End: levels.length - 1 } as Record<string, number>
        )[event.key];
        if (target === undefined) return;
        event.preventDefault();
        const level = levels[(target + levels.length) % levels.length];
        void this.form.useLevel(level);
        (event.currentTarget as HTMLElement).querySelector<HTMLElement>('#settings-level-' + level)?.focus();
    }

    /**
     * Saves the settings of every sub-page. Errors show by their fields: when the settings shown have none, the first
     * sub-page with errors opens; the first error is brought into view.
     */
    protected async save(): Promise<void> {
        if ((await this.form.save()) !== 'invalid') return;
        const faulty = this.form.faultyTabs();
        const shown = this.settingsShown();
        if (faulty.size && !(shown && faulty.has(shown))) {
            const first = locateSettings(TABS.find((tab) => faulty.has(tab.id))!.id)!;
            await this.go(first.section.id, first.subpage.id);
        }
        afterNextRender(
            () => {
                const first =
                    this.host.querySelector('.admin-panel .field-error')?.closest('.field, .check') ??
                    this.host.querySelector('.admin-panel .settings-group .form-error') ??
                    this.host.querySelector('.settings-summary');
                first?.scrollIntoView({ block: 'center' });
            },
            { injector: this.injector },
        );
    }

    protected onBeforeUnload(event: BeforeUnloadEvent): void {
        if (this.form.changed()) event.preventDefault();
    }
}

/** Scrolls the line of chips (phones) so that `chip` is fully in view, with a little room beside it. */
export function showChip(chip: HTMLElement | null): void {
    const strip = chip?.parentElement;
    if (!chip || !strip) return;
    const box = strip.getBoundingClientRect();
    const own = chip.getBoundingClientRect();
    if (own.left < box.left) strip.scrollLeft += own.left - box.left - 12;
    else if (own.right > box.right) strip.scrollLeft += own.right - box.right + 12;
}
