import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { visibleSections } from '../../admin/admin-sections';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { ChangeFeed } from '../change-feed.service';
import { Dialogs } from '../dialogs.service';
import { ListingStore } from '../listing.store';
import { ScanService } from '../scan/scan.service';
import { Popovers } from './popovers.service';

/**
 * The ⋮ menu: who is signed in, scanning the folder browsed or taking it out of the index, the other spaces of the
 * account, full screen, the administration page (settings, tags, duplicates, background tasks, people...), signing
 * out. Each entry shows only with its right. While keep alive pings the library folder, a dot on the button says how it
 * goes: green, or a yellow ! when the folder doesn't answer (and a line in the menu, a link for `settings.keepAlive`).
 */
@Component({
    selector: 'sfe-main-menu',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'menu' },
    template: `
        <button type="button" class="icon-button" aria-haspopup="menu" [title]="buttonTitle()" aria-controls="menu" [attr.aria-label]="buttonLabel()"
                [attr.aria-expanded]="isOpen()" (click)="popovers.toggle('menu')">
            <svg sfeIcon="more"></svg>
            @if (keepAlive() !== 'none') {
                <span class="menu-keep-alive" [class.failing]="keepAlive() === 'failing'" aria-hidden="true">{{ keepAlive() === 'failing' ? '!' : '' }}</span>
            }
        </button>
        <div class="popover menu-list" id="menu" role="menu" [hidden]="!isOpen()">
            @if (accountVisible()) {
                <a class="menu-account" role="menuitem" routerLink="/admin" [queryParams]="{ section: 'account' }" [state]="{ sfeFromLibrary: true }"
                   title="My account: your preferences and your password" (click)="popovers.close()">
                    <svg sfeIcon="user"></svg>
                    <span>
                        {{ user()!.displayName || user()!.userName }}
                        <small>{{ config()?.tenant?.name }}</small>
                    </span>
                </a>
            } @else {
                <p class="menu-account" role="none">
                    <svg sfeIcon="user"></svg>
                    <span>
                        {{ user() ? (user()!.displayName || user()!.userName) : 'Guest' }}
                        <small>{{ config()?.tenant?.name }}</small>
                    </span>
                </p>
            }
            @if (keepAlive() !== 'none') {
                @if (canSeeKeepAlive()) {
                    <a class="menu-keep-alive-line" role="menuitem" routerLink="/admin" [queryParams]="{ section: 'keepAlive' }" [state]="{ sfeFromLibrary: true }"
                       [class.failing]="keepAlive() === 'failing'" title="Keep alive: what the last pings of the library folder found" (click)="popovers.close()">
                        <span class="menu-keep-alive" [class.failing]="keepAlive() === 'failing'" aria-hidden="true">{{ keepAlive() === 'failing' ? '!' : '' }}</span>{{ keepAliveText() }}
                    </a>
                } @else {
                    <p class="menu-keep-alive-line" role="none" [class.failing]="keepAlive() === 'failing'">
                        <span class="menu-keep-alive" [class.failing]="keepAlive() === 'failing'" aria-hidden="true">{{ keepAlive() === 'failing' ? '!' : '' }}</span>{{ keepAliveText() }}
                    </p>
                }
            }
            @if (canScan() || canRemove() || canExclude()) {
                <hr>
            }
            @if (canScan()) {
                <button type="button" role="menuitem" [disabled]="scan.busy()" [title]="scan.running() ? 'A scan is in progress' : 'Index this folder and its sub-folders for search (asks first)'" (click)="run(startScan)">
                    <svg sfeIcon="scan"></svg>Scan this folder
                </button>
            }
            @if (canRemove()) {
                <button type="button" role="menuitem" [disabled]="scan.running()" [title]="scan.running() ? 'A scan is in progress' : 'Take this folder and its sub-folders out of the search index; the files stay on the disk (asks first)'" (click)="run(removeFromIndex)">
                    <svg sfeIcon="close"></svg>Remove from index…
                </button>
            }
            @if (canExclude()) {
                <button type="button" role="menuitem" title="Keep this folder out of scans, trickplay or thumbnails, for the whole space" (click)="run(excludeFolder)">
                    <svg sfeIcon="scan"></svg>Scans and previews…
                </button>
            }
            @if (otherTenants().length) {
                <hr>
                @for (tenant of otherTenants(); track tenant.slug) {
                    <button type="button" role="menuitem" [title]="'Switch to the space ' + tenant.name + ': its own library, tags and settings'" (click)="switchTo(tenant.slug)">
                        <svg sfeIcon="space"></svg>{{ tenant.name }}
                    </button>
                }
            }
            <hr>
            @if (adminVisible()) {
                <a role="menuitem" routerLink="/admin" [state]="{ sfeFromLibrary: true }" (click)="popovers.close()"
                   title="Settings, tags, duplicates & moved files, background tasks, spaces & accounts, your account and more">
                    <svg sfeIcon="settings"></svg>Administration
                </a>
            }
            <button type="button" role="menuitem" title="Show the page full screen, or leave full screen" (click)="run(toggleFullscreen)"><svg sfeIcon="fullscreen"></svg>Toggle fullscreen</button>
            @if (user()) {
                <button type="button" role="menuitem" title="Sign out of this browser" (click)="run(signOut)"><svg sfeIcon="signout"></svg>Sign out</button>
            } @else {
                <a role="menuitem" routerLink="/login" title="Sign in with an account" (click)="popovers.close()"><svg sfeIcon="user"></svg>Sign in</a>
            }
            <p class="menu-version" role="none" [title]="versionTitle()">SFE {{ config()?.version }}</p>
        </div>
    `,
})
export class MainMenuComponent {
    protected readonly popovers = inject(Popovers);
    protected readonly scan = inject(ScanService);
    private readonly dialogs = inject(Dialogs);
    private readonly configService = inject(ConfigService);
    private readonly api = inject(Api);
    private readonly router = inject(Router);
    private readonly toasts = inject(ToastService);
    protected readonly config = this.configService.config;

    protected readonly isOpen = computed(() => this.popovers.open() === 'menu');
    protected readonly user = this.configService.user;
    protected readonly canScan = computed(() => this.configService.rights().has(Rights.scan));
    protected readonly canRemove = computed(() => this.configService.rights().has(Rights.removeFromIndex));
    /** The folder browsed (not the library itself, nor a search) may be kept out of scans and previews. */
    private readonly store = inject(ListingStore);
    protected readonly canExclude = computed(() => {
        const listing = this.store.listing();
        return !!listing && !this.store.searching() && !!this.store.route().path && this.store.can(Rights.excludeFolders);
    });
    protected readonly adminVisible = computed(() => {
        const rights = this.configService.rights();
        return visibleSections(any => any.some(right => rights.has(right))).length > 0;
    });
    /** The account line links to Administration › My account when signed in with one of its rights. */
    protected readonly accountVisible = computed(() => {
        const rights = this.configService.rights();
        return !!this.user() && visibleSections(any => any.some(right => rights.has(right))).some(section => section.id === 'account');
    });
    protected readonly otherTenants = computed(() => {
        const config = this.config();
        return config?.tenants.filter(tenant => tenant.id !== config.tenant?.id) ?? [];
    });
    /** How keep alive goes (the change feed): a dot on the button, a line in the menu; nothing when it is off. */
    protected readonly keepAlive = inject(ChangeFeed).keepAlive;
    protected readonly keepAliveText = computed(() => this.keepAlive() === 'failing' ? 'Network folder not answering' : 'Network folder answering');
    protected readonly canSeeKeepAlive = computed(() => this.configService.rights().has(Rights.keepAliveSettings));
    protected readonly buttonTitle = computed(() => {
        const title = 'Scan this folder, administration, your account and more';
        return this.keepAlive() === 'none' ? title : `${title} · ${this.keepAliveText()}`;
    });
    protected readonly buttonLabel = computed(() => this.keepAlive() === 'failing' ? 'More (network folder not answering)' : 'More');
    protected readonly versionTitle = computed(() => {
        const config = this.config();
        return config?.commit ? `Version ${config.version}, commit ${config.commit}` : `Version ${config?.version}`;
    });

    protected readonly startScan = () => this.scan.start();
    protected readonly removeFromIndex = () => this.dialogs.removeFromIndex()?.open();
    protected readonly excludeFolder = () => {
        const listing = this.store.listing();
        if (listing && 'exclusions' in listing)
            this.dialogs.folderExclusions()?.open(listing.path, listing.exclusions);
        else
            this.dialogs.folderExclusions()?.open(this.store.route().path, null);
    };
    protected readonly toggleFullscreen = () => {
        if (document.fullscreenElement)
            void document.exitFullscreen();
        else
            void document.documentElement.requestFullscreen?.();
    };
    protected readonly signOut = async () => {
        try {
            await this.api.signOut();
            this.configService.clear();
            await this.router.navigate(['/login']);
        } catch (error) {
            this.toasts.error(`Could not sign out: ${(error as Error).message}`);
        }
    };

    /** Closes the menu, then runs the entry's action. */
    protected run(action: () => unknown): void {
        this.popovers.close();
        action();
    }

    /** Another space of the account: the page starts again there (its library, settings and tags are its own). */
    protected async switchTo(slug: string): Promise<void> {
        this.popovers.close();
        try {
            await this.api.switchTenant(slug);
            window.location.assign(document.baseURI);
        } catch (error) {
            this.toasts.error(`Could not switch the space: ${(error as Error).message}`);
        }
    }
}
