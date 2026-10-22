import { Location, NgTemplateOutlet } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, Injector, OnInit, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { redirected } from '../core/api.service';
import { SettingLevel } from '../core/models';
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
import { IconComponent } from '../shared/icon.component';
import { TagEditorDialog } from '../tags/tag-editor.dialog';
import { TagListComponent } from '../tags/tag-list.component';
import { TasksSectionComponent } from '../tasks/tasks-section.component';
import { TrackingSectionComponent } from '../tracking/tracking-section.component';
import { TrickplayActivityComponent } from '../trickplay/trickplay-activity.component';
import { KeepAliveStatusComponent } from './keep-alive-status.component';
import { AccountSectionComponent } from '../account/account-section.component';
import { AccessSectionComponent } from '../access/access-section.component';
import { ADMIN_SEARCH_LIMIT, AdminSearchResult, markWords, searchAdmin, searchWords } from './admin-search';
import { ADMIN_SECTIONS, AdminSubpage, findSection, findSubpage, groupId, groupsOf, locateSettings, movedPage, visibleSections } from './admin-sections';
import { SystemSectionComponent } from './system-section.component';
import { TrashSectionComponent } from '../files/trash-section.component';
import { UploadsSectionComponent } from '../files/uploads-section.component';
import { VideoStreamsComponent } from './video-streams.component';
import { ExclusionsSectionComponent } from '../library/scan/exclusions-section.component';

/** History state of the entries of this page opened from the listing page: "back" returns there. */
const FROM_LIBRARY = 'sfeFromLibrary';
/** History state of a section opened from the list of sections (phones): "back" returns to the list. */
const FROM_LIST = 'sfeFromList';
/** Phones: the sections are a list of their own instead of a sidebar (the breakpoint of admin.css). */
const PHONE = '(max-width: 760px)';

/** The level tabs' names, in the save bar. */
const LEVEL_TITLES: Partial<Record<SettingLevel, string>> = { tenant: 'This space', server: 'Whole server' };

export { leaveAdminPage } from './leave-admin-page';

/**
 * The administration page (`admin?section=&page=`): the sections of admin-sections.ts in a sidebar, the sub-pages of
 * the open section listed under it (on phones, where the sections are a list of their own, as chips above the
 * sub-page); breadcrumbs above the heading say where the page is. Without a section, it shows the sections as cards by
 * group (the overview; on phones, the list of sections), unless the session sees only one. A search box above the sections finds sections,
 * sub-pages and settings (admin-search.ts). The settings sub-pages share one form (SettingsForm), saved from the bar at
 * the bottom. Moving between sections and sub-pages replaces the history entry, so that "back" returns to the listing page.
 */
@Component({
    selector: 'sfe-admin-page',
    imports: [RouterLink, NgTemplateOutlet, IconComponent, EmptyStateComponent, SettingsPanelComponent, TagListComponent, TagEditorDialog,
        TrackingSectionComponent, TasksSectionComponent, TrickplayActivityComponent, KeepAliveStatusComponent, SystemSectionComponent, AccessSectionComponent,
        AccountSectionComponent, LogsSectionComponent, VideoStreamsComponent, TrashSectionComponent, UploadsSectionComponent, ExclusionsSectionComponent],
    providers: [SettingsForm],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'admin-page', '(window:beforeunload)': 'onBeforeUnload($event)' },
    template: `
        @if (unreachable(); as error) {
            <sfe-empty-state icon="close" heading="The server is not reachable" [text]="error" />
        } @else if (ready() && empty()) {
            <sfe-empty-state icon="lock" heading="Nothing to administer" text="This session has no rights to change the space, its settings or the server." />
        } @else if (ready()) {
            <header class="topbar admin-topbar">
                <button type="button" class="icon-button ghost" [attr.aria-label]="backHint()" [title]="backHint()" (click)="back()">
                    <svg sfeIcon="left"></svg>
                </button>
                <span class="brand"><svg sfeIcon="folder"></svg><span>SFE</span></span>
                <h1 class="admin-title">Administration</h1>
            </header>
            <div class="admin-layout" [class.section-list]="sectionList()">
                @if (!phone() || sectionList()) {
                    <nav class="admin-nav" aria-label="Administration sections">
                        <div class="admin-search" role="search">
                            <svg sfeIcon="search" class="search-icon"></svg>
                            <input #searchBox type="search" placeholder="Find a setting…" aria-label="Find a setting or a page" aria-controls="admin-search-results"
                                   title="Find the settings and pages whose name or description has these words (Enter opens the first one, Esc clears)"
                                   spellcheck="false" autocomplete="off" enterkeyhint="go" [value]="query()" (input)="query.set(valueOf($event))" (keydown)="onSearchKey($event)">
                            @if (query()) {
                                <button type="button" class="icon-button ghost small admin-search-clear" aria-label="Clear the search"
                                        title="Clear the search and show the sections again (Esc)" (click)="query.set(''); searchBox.focus()">
                                    <svg sfeIcon="close"></svg>
                                </button>
                            }
                        </div>
                        @if (searching()) {
                            <p class="admin-search-count" role="status">{{ resultsCount() }}</p>
                            <ul class="admin-search-results" id="admin-search-results" aria-label="Found">
                                @for (result of shownResults(); track $index) {
                                    <li>
                                        <a routerLink="/admin" [queryParams]="resultParams(result)" [state]="linkState()" [replaceUrl]="!phone()"
                                           [title]="'Open ' + result.where + (result.setting ? ' at this setting' : '')" (click)="found(result)">
                                            <span class="admin-search-title"><ng-container [ngTemplateOutlet]="markedText" [ngTemplateOutletContext]="{ $implicit: result.title }" /></span>
                                            <small class="admin-search-where">{{ result.where }}</small>
                                            @if (result.text) {
                                                <small class="admin-search-text"><ng-container [ngTemplateOutlet]="markedText" [ngTemplateOutletContext]="{ $implicit: result.text }" /></small>
                                            }
                                        </a>
                                    </li>
                                }
                            </ul>
                        }
                        @for (group of searching() ? [] : groups(); track group.title) {
                            <h2>{{ group.title }}</h2>
                            <ul>
                                @for (item of group.sections; track item.id) {
                                    @let open = !phone() && !overview() && item.id === section().id;
                                    <li>
                                        <a routerLink="/admin" [queryParams]="{ section: item.id }" [state]="linkState()" [replaceUrl]="!phone()"
                                           [attr.aria-current]="open && item.subpages.length === 1 ? 'page' : null" [class.open]="open" [title]="item.description"
                                           [class.has-error]="faulty(item.subpages)">
                                            <svg [sfeIcon]="item.icon"></svg>
                                            <span>{{ item.title }}</span>
                                            @if (phone()) {
                                                <small class="admin-nav-description">{{ item.description }}</small>
                                            }
                                            <!-- The marks of an open section are on its sub-pages. -->
                                            @if (!open || item.subpages.length === 1) {
                                                <ng-container [ngTemplateOutlet]="mark" [ngTemplateOutletContext]="{ $implicit: item.subpages }" />
                                            }
                                            @if (phone()) {
                                                <svg sfeIcon="right" class="admin-nav-chevron"></svg>
                                            }
                                        </a>
                                        @if (open && item.subpages.length > 1) {
                                            <ul class="admin-subnav" [attr.aria-label]="item.title">
                                                @for (page of item.subpages; track page.id) {
                                                    <li>
                                                        <a routerLink="/admin" [queryParams]="{ section: item.id, page: page.id === item.subpages[0].id ? null : page.id }"
                                                           [state]="linkState()" replaceUrl [attr.aria-current]="page.id === subpage().id ? 'page' : null"
                                                           [class.has-error]="faulty([page])">
                                                            <span>{{ page.title }}</span>
                                                            @if (badge(page.id); as badge) {
                                                                <span class="tracking-badge">{{ badge }}</span>
                                                            }
                                                            <ng-container [ngTemplateOutlet]="mark" [ngTemplateOutletContext]="{ $implicit: [page] }" />
                                                        </a>
                                                    </li>
                                                }
                                            </ul>
                                        }
                                    </li>
                                }
                            </ul>
                        }
                    </nav>
                }

                <main class="admin-main">
                    @if (overview()) {
                        <header class="admin-head">
                            <h2>Administration</h2>
                            <p>Everything this account can change or look at, by section.</p>
                        </header>
                        <div class="admin-overview">
                            @for (group of groups(); track group.title) {
                                <section class="admin-overview-group" [attr.aria-labelledby]="groupId(group.title)">
                                    <h3 [id]="groupId(group.title)">{{ group.title }}</h3>
                                    <div class="admin-cards">
                                        @for (item of group.sections; track item.id) {
                                            <article class="admin-card" [class.has-error]="faulty(item.subpages)">
                                                <a class="admin-card-title" routerLink="/admin" [queryParams]="{ section: item.id }" [state]="historyState" replaceUrl
                                                   [title]="'Open ' + item.title">
                                                    <svg [sfeIcon]="item.icon"></svg>
                                                    <span>{{ item.title }}</span>
                                                    <ng-container [ngTemplateOutlet]="mark" [ngTemplateOutletContext]="{ $implicit: item.subpages }" />
                                                </a>
                                                <p>{{ item.description }}</p>
                                                @if (item.subpages.length > 1) {
                                                    <ul class="admin-card-pages" [attr.aria-label]="item.title">
                                                        @for (page of item.subpages; track page.id) {
                                                            <li>
                                                                <a routerLink="/admin" [queryParams]="{ section: item.id, page: page.id === item.subpages[0].id ? null : page.id }"
                                                                   [state]="historyState" replaceUrl [title]="page.description ?? ''" [class.has-error]="faulty([page])">{{ page.title }}</a>
                                                            </li>
                                                        }
                                                    </ul>
                                                }
                                            </article>
                                        }
                                    </div>
                                </section>
                            }
                        </div>
                    } @else if (!sectionList()) {
                        <header class="admin-head">
                            <!-- Where this page is: Administration › group › section › sub-page (phones leave the group out). -->
                            <nav class="admin-crumbs" aria-label="Breadcrumb">
                                <ol>
                                    <li>
                                        @if (phone()) {
                                            <button type="button" class="admin-crumb-link" title="Show the list of sections" (click)="back()">Administration</button>
                                        } @else {
                                            <a routerLink="/admin" [state]="historyState" replaceUrl title="Show every section">Administration</a>
                                        }
                                    </li>
                                    <li class="admin-crumb-group">
                                        @if (phone()) {
                                            {{ section().group }}
                                        } @else {
                                            <a routerLink="/admin" [fragment]="groupId(section().group)" [state]="historyState" replaceUrl
                                               [title]="'Show the sections of ' + section().group">{{ section().group }}</a>
                                        }
                                    </li>
                                    @if (section().subpages.length > 1) {
                                        <li>
                                            <a routerLink="/admin" [queryParams]="{ section: section().id }" [state]="stayState()" replaceUrl
                                               [title]="'Open the first page of ' + section().title">{{ section().title }}</a>
                                        </li>
                                        <li aria-current="page">{{ subpage().title }}</li>
                                    } @else {
                                        <li aria-current="page">{{ section().title }}</li>
                                    }
                                </ol>
                            </nav>
                            <h2>{{ section().subpages.length > 1 ? subpage().title : section().title }}</h2>
                            <p>{{ subpage().description ?? section().description }}</p>
                        </header>
                        <!-- Phones have no sidebar: the sub-pages are chips. -->
                        @if (phone() && section().subpages.length > 1) {
                            <div class="settings-tabs admin-chips" role="tablist" [attr.aria-label]="section().title" (keydown)="onChipKey($event)">
                                @for (item of section().subpages; track item.id) {
                                    <button type="button" role="tab" class="settings-tab" [id]="'admin-chip-' + item.id" aria-controls="admin-panel"
                                            [attr.aria-selected]="subpage().id === item.id" [tabIndex]="subpage().id === item.id ? 0 : -1"
                                            [class.has-error]="faulty([item])" (click)="select(item.id)">
                                        {{ item.title }}
                                        @if (badge(item.id); as badge) {
                                            <span class="tracking-badge">{{ badge }}</span>
                                        }
                                        @if (faulty([item])) {
                                            <span class="settings-tab-error"><span class="visually-hidden"> (needs fixing)</span></span>
                                        }
                                    </button>
                                }
                            </div>
                        }

                        <div id="admin-panel" class="admin-panel" [attr.role]="phone() && section().subpages.length > 1 ? 'tabpanel' : null"
                             [attr.aria-labelledby]="phone() && section().subpages.length > 1 ? 'admin-chip-' + subpage().id : null">
                            @if (subpage().settings; as settings) {
                                @if (form.levels().length > 1) {
                                    <div class="settings-tabs settings-level" role="tablist" aria-label="Settings of" (keydown)="onLevelKey($event)">
                                        <button type="button" role="tab" class="settings-tab" id="settings-level-tenant" aria-controls="settings-level-panel"
                                                [attr.aria-selected]="form.level() === 'tenant'" [tabIndex]="form.level() === 'tenant' ? 0 : -1"
                                                title="This space's own settings, over the server's: they apply to this space only" (click)="form.useLevel('tenant')">
                                            This space{{ config.tenant() ? ' (' + config.tenant()!.name + ')' : '' }}
                                            <ng-container [ngTemplateOutlet]="levelMark" [ngTemplateOutletContext]="{ $implicit: 'tenant' }" />
                                        </button>
                                        <button type="button" role="tab" class="settings-tab" id="settings-level-server" aria-controls="settings-level-panel"
                                                [attr.aria-selected]="form.level() === 'server'" [tabIndex]="form.level() === 'server' ? 0 : -1"
                                                title="The server's settings: the defaults of every space, and what the whole server shares" (click)="form.useLevel('server')">
                                            Whole server
                                            <ng-container [ngTemplateOutlet]="levelMark" [ngTemplateOutletContext]="{ $implicit: 'server' }" />
                                        </button>
                                    </div>
                                }
                                <sfe-settings-panel [tab]="settings" (submitted)="save()" (activity)="select('activity')"
                                                    [attr.id]="form.levels().length > 1 ? 'settings-level-panel' : null"
                                                    [attr.role]="form.levels().length > 1 ? 'tabpanel' : null"
                                                    [attr.aria-labelledby]="form.levels().length > 1 ? 'settings-level-' + form.level() : null" />
                            } @else {
                                @switch (section().id) {
                                    @case ('tags') {
                                        <sfe-tag-list />
                                    }
                                    @case ('tracking') {
                                        <sfe-tracking-section [tab]="subpage().id" />
                                    }
                                    @case ('trickplay') {
                                        <sfe-trickplay-activity />
                                    }
                                    @case ('playback') {
                                        <sfe-video-streams />
                                    }
                                    @case ('tasks') {
                                        <sfe-tasks-section [tab]="subpage().id" (selectTab)="select($event)" (openTrickplay)="go('trickplay', 'activity')" />
                                    }
                                    @case ('logs') {
                                        <sfe-logs-section (openSettings)="select('settings')" />
                                    }
                                    @case ('system') {
                                        <sfe-system-section [tab]="subpage().id" />
                                    }
                                    @case ('access') {
                                        <sfe-access-section [tab]="subpage().id" />
                                    }
                                    @case ('account') {
                                        <sfe-account-section [tab]="subpage().id" />
                                    }
                                    @case ('keepAlive') {
                                        <sfe-keep-alive-status (openSettings)="select('settings')" />
                                    }
                                    @case ('scanning') {
                                        <sfe-exclusions-section />
                                    }
                                    @case ('files') {
                                        @if (subpage().id === 'uploads') {
                                            <sfe-uploads-section />
                                        } @else {
                                            <sfe-trash-section />
                                        }
                                    }
                                }
                            }
                        </div>
                    }

                    @if (settingsShown() || form.changed() || form.summary()) {
                        <footer class="admin-savebar" [class.changed]="form.changed()">
                            @if (form.summary(); as summary) {
                                <p class="form-error settings-summary" role="alert">{{ summary }}</p>
                            }
                            <span class="admin-savebar-status">{{ saveStatus() }}</span>
                            <span class="spacer"></span>
                            @if (settingsShown()) {
                                <label class="check settings-show-all" title="Show every setting instead of only the most used ones">
                                    <input type="checkbox" [checked]="form.showAll()" (change)="form.setShowAll(checked($event))"><span>Show all settings</span>
                                </label>
                                <button type="button" class="button" [disabled]="!form.customized()"
                                        [title]="form.customized() ? resetHint() : 'Nothing saved yet'" (click)="form.reset()">Reset to defaults</button>
                            }
                            @if (form.changed()) {
                                <button type="button" class="button" [title]="form.levels().length > 1 ? 'Undo the changes not saved yet, on every page of both tabs' : 'Undo the changes not saved yet, on every page'" (click)="form.discard()">Discard</button>
                            }
                            <button type="button" class="button primary" [title]="saveHint()"
                                    [disabled]="form.saving() || !form.changed()" (click)="save()">Save</button>
                        </footer>
                    }
                </main>
            </div>
            <sfe-tag-editor />
        }

        <!-- Text with the words searched for marked. -->
        <ng-template #markedText let-text>
            <!-- On one line: no spaces between the parts. -->
            @for (part of marked(text); track $index) {@if (part.match) {<mark>{{ part.text }}</mark>} @else {<span>{{ part.text }}</span>}}
        </ng-template>

        <!-- A dot on a level's tab while it has changes not saved yet (they stay while the other level shows). -->
        <ng-template #levelMark let-level>
            @if (form.changedLevels().has(level)) {
                <span class="admin-mark" title="Changed settings, not saved yet"><span class="visually-hidden"> (not saved)</span></span>
            }
        </ng-template>

        <!-- A dot for settings changed and not saved yet in these sub-pages, red when some need fixing. -->
        <ng-template #mark let-pages>
            @if (faulty(pages)) {
                <span class="admin-mark error" title="Some settings here need fixing"><span class="visually-hidden"> (needs fixing)</span></span>
            } @else if (changed(pages)) {
                <span class="admin-mark" title="Changed settings, not saved yet"><span class="visually-hidden"> (not saved)</span></span>
            }
        </ng-template>
    `,
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
        const visible = visibleSections(rights => this.config.canAny(rights));
        return visible.length ? visible : ADMIN_SECTIONS;
    });
    protected readonly groups = computed(() => groupsOf(this.sections()));
    /** Nothing here for this session (a visitor without an account). */
    protected readonly empty = computed(() => visibleSections(rights => this.config.canAny(rights)).length === 0);
    protected readonly ready = signal(false);
    protected readonly unreachable = signal<string | null>(null);

    private readonly route = inject(ActivatedRoute);
    private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });
    /** The group the overview scrolls to (the breadcrumbs' group link). */
    private readonly fragment = toSignal(this.route.fragment, { requireSync: true });
    protected readonly section = computed(() => findSection(this.params().get('section'), this.sections()));
    protected readonly subpage = computed(() => findSubpage(this.section(), this.params().get('page')));

    private readonly location = inject(Location);
    /** Opened from the listing page: kept in the entries this page replaces, so that "back" still returns there. */
    private readonly fromLibrary = Boolean((this.location.getState() as Record<string, unknown> | null)?.[FROM_LIBRARY]);
    protected readonly historyState = { [FROM_LIBRARY]: this.fromLibrary };

    /**
     * On phones there is no room for the sidebar: the page without a section is the list of sections, and each section
     * is a page of its own, opened from it as a new history entry so that "back" (the browser's too) returns to it.
     */
    protected readonly phone = signal(false);
    protected readonly sectionList = computed(() => this.phone() && !this.params().has('section'));
    /** Computers, without a section: the sections as cards, by group (a session seeing one section gets it instead). */
    protected readonly overview = computed(() => !this.phone() && !this.params().has('section') && this.sections().length > 1);
    protected readonly groupId = groupId;
    protected readonly linkState = computed(() => this.sectionList() ? { ...this.historyState, [FROM_LIST]: true } : this.historyState);
    protected readonly backHint = computed(() => this.phone() && !this.sectionList() ? 'Back to the sections' : 'Back to the library');
    /** The settings tab of settings-schema.ts on screen, if any. */
    protected readonly settingsShown = computed(() => this.sectionList() || this.overview() ? undefined : this.subpage().settings);

    /** The search box's text, and what it finds among the sections the session sees. */
    protected readonly query = signal('');
    private readonly words = computed(() => searchWords(this.query()));
    protected readonly searching = computed(() => this.words().length > 0);
    private readonly results = computed(() => searchAdmin(this.query(), this.sections()));
    protected readonly shownResults = computed(() => this.results().slice(0, ADMIN_SEARCH_LIMIT));
    protected readonly resultsCount = computed(() => {
        const count = this.results().length;
        return count === 0 ? 'Nothing found'
            : count > ADMIN_SEARCH_LIMIT ? `The first ${ADMIN_SEARCH_LIMIT} of ${count} found: add words to narrow them`
                : count === 1 ? '1 found' : `${count} found`;
    });
    /** The setting picked among the results, brought into view once its sub-page shows it. */
    private readonly reveal = signal<string | null>(null);
    protected readonly valueOf = valueOf;

    protected readonly saveStatus = computed(() => {
        const changed = [...this.form.changedTabs()].map(id => this.titleOfSettings(id)).filter(Boolean);
        const others = [...this.form.changedLevels()].filter(level => level !== this.form.level()).map(level => LEVEL_TITLES[level]);
        if (changed.length)
            return `Not saved yet: ${changed.join(', ')}${others.length ? `; also in ${others.join(', ')}` : ''}`;
        if (others.length)
            return `Not saved yet in ${others.join(', ')}`;
        // Servers without levels answer without states: their settings are the server's.
        if (this.form.states() && this.form.level() === 'tenant')
            return this.form.customized() ? 'This space has settings of its own' : 'Following the server\'s settings';
        return this.form.customized() ? 'Using saved settings' : 'Using the server\'s configuration';
    });
    protected readonly resetHint = computed(() => this.form.states() && this.form.level() === 'tenant'
        ? 'Forget the space\'s own settings and follow the server\'s again (asks first)'
        : 'Forget the saved settings and go back to the server\'s configuration (asks first)');
    protected readonly saveHint = computed(() => this.form.changedLevels().size > 1
        ? 'Save and apply the settings changed in both tabs: the whole server\'s, then this space\'s'
        : this.form.states() && this.form.level() === 'tenant'
            ? 'Save and apply the settings of every page, for everyone using this space'
            : 'Save and apply the settings of every page, for everyone using this server');

    constructor() {
        const title = inject(Title);
        effect(() => {
            if (this.overview() || this.sectionList())
                return title.setTitle('Administration – SFE');
            const section = this.section();
            const page = section.subpages.length > 1 ? `${this.subpage().title} – ` : '';
            title.setTitle(`${page}${section.title} – Administration – SFE`);
        });
        // The address of a sub-page that moved (a bookmark, history): replaced by where it is now.
        effect(() => {
            const params = this.params();
            const moved = movedPage(params.get('section'), params.get('page'));
            if (moved)
                untracked(() => void this.go(moved.section, moved.page));
        });
        // The overview opens at its top, or at the group of the breadcrumbs' link.
        effect(() => {
            if (!this.overview())
                return;
            const fragment = this.fragment();
            afterNextRender(() => {
                const group = fragment && document.getElementById(fragment);
                if (group)
                    group.scrollIntoView({ block: 'start' });
                else
                    window.scrollTo({ top: 0 });
            }, { injector: this.injector });
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
            if (untracked(this.phone))
                window.scrollTo({ top: 0 });
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
            this.theme.setDefault((this.config.config() ?? await this.config.load()).theme);
        } catch (error) {
            if (!redirected(error))
                this.unreachable.set((error as Error).message);
            return;
        }
        this.ready.set(true);
        void this.form.load();
    }

    /** False when the user would rather stay, to save the settings changed. */
    canLeave(): boolean {
        return !this.form.changed() || confirm('Leave without saving the settings you changed?');
    }

    /** Some settings of `pages` need fixing. */
    protected faulty(pages: AdminSubpage[]): boolean {
        const faulty = this.form.faultyTabs();
        return pages.some(page => page.settings && faulty.has(page.settings));
    }

    /** Some settings of `pages` are changed and not saved yet. */
    protected changed(pages: AdminSubpage[]): boolean {
        const changed = this.form.changedTabs();
        return pages.some(page => page.settings && changed.has(page.settings));
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
        return { section: result.section.id, page: result.subpage.id === result.section.subpages[0].id ? null : result.subpage.id };
    }

    /** A result is opened: a setting is brought into view, in the full view when the simple one hides it. */
    protected found(result: AdminSearchResult): void {
        if (!result.setting)
            return;
        // As for an error on a hidden setting: shown without remembering the choice.
        if (!SIMPLE_SETTINGS.has(result.setting))
            this.form.showAll.set(true);
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
            if (!first)
                return;
            this.found(first);
            void this.router.navigate(['/admin'], { queryParams: this.resultParams(first), state: this.linkState(), replaceUrl: !this.phone() });
        }
    }

    /** Scrolls to the field of the setting `key` and focuses it, once its sub-page shows it. */
    private revealSetting(key: string): void {
        const control = this.host.querySelector<HTMLElement>(`#${settingFieldId(key)}`);
        const field = control?.closest<HTMLElement>('.field, .setting-check');
        if (!control || !field || this.reveal() !== key)
            return;
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
        return found && (found.section.subpages.length > 1 ? `${found.section.title} › ${found.subpage.title}` : found.section.title);
    }

    /** Shows the sub-page `page` of the current section. */
    protected select(page: string, { focus = false } = {}): void {
        void this.go(this.section().id, page).then(() => {
            if (focus)
                afterNextRender(() => document.getElementById(`admin-chip-${page}`)?.focus(), { injector: this.injector });
        });
    }

    protected go(section: string, page?: string): Promise<boolean> {
        const target = findSection(section, this.sections());
        return this.router.navigate(['/admin'], {
            queryParams: { section: target.id, page: page && page !== target.subpages[0].id ? page : null },
            replaceUrl: true,
            state: this.stayState(),
        });
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
            if (this.openedFromList())
                history.back();
            else
                void this.router.navigate(['/admin'], { replaceUrl: true, state: this.historyState });
        } else if (this.fromLibrary)
            history.back();
        else
            void this.router.navigate(['/'], { queryParams: routeParams(this.store.route()) });
    }

    /** Arrow keys move between the chips (the WAI-ARIA tabs pattern); Tab goes into the panel. */
    protected onChipKey(event: KeyboardEvent): void {
        const ids = this.section().subpages.map(page => page.id);
        const index = ids.indexOf(this.subpage().id);
        const target = ({ ArrowLeft: index - 1, ArrowRight: index + 1, Home: 0, End: ids.length - 1 } as Record<string, number>)[event.key];
        if (target === undefined)
            return;
        event.preventDefault();
        this.select(ids[(target + ids.length) % ids.length], { focus: true });
    }

    /** Arrow keys, Home and End move between the level tabs (this space, whole server), as between the chips. */
    protected onLevelKey(event: KeyboardEvent): void {
        const levels = this.form.levels();
        const index = levels.indexOf(this.form.level());
        const target = ({ ArrowLeft: index - 1, ArrowRight: index + 1, Home: 0, End: levels.length - 1 } as Record<string, number>)[event.key];
        if (target === undefined)
            return;
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
        if (await this.form.save() !== 'invalid')
            return;
        const faulty = this.form.faultyTabs();
        const shown = this.settingsShown();
        if (faulty.size && !(shown && faulty.has(shown))) {
            const first = locateSettings(TABS.find(tab => faulty.has(tab.id))!.id)!;
            await this.go(first.section.id, first.subpage.id);
        }
        afterNextRender(() => {
            const first = this.host.querySelector('.admin-panel .field-error')?.closest('.field, .check')
                ?? this.host.querySelector('.admin-panel .settings-group .form-error')
                ?? this.host.querySelector('.settings-summary');
            first?.scrollIntoView({ block: 'center' });
        }, { injector: this.injector });
    }

    protected onBeforeUnload(event: BeforeUnloadEvent): void {
        if (this.form.changed())
            event.preventDefault();
    }
}
