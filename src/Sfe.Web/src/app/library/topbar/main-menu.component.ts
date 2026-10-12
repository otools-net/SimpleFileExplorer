import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { visibleSections } from '../../admin/admin-sections';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { Rights } from '../../core/rights';
import { ToastService } from '../../core/toast.service';
import { IconComponent } from '../../shared/icon.component';
import { Dialogs } from '../dialogs.service';
import { ScanService } from '../scan/scan.service';
import { Popovers } from './popovers.service';

/**
 * The ⋮ menu: who is signed in, scanning the folder browsed or taking it out of the index, the other spaces of the
 * account, full screen, the administration page (settings, tags, duplicates, background tasks, people...), signing
 * out. Each entry shows only with its right.
 */
@Component({
    selector: 'sfe-main-menu',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'menu' },
    template: `
        <button type="button" class="icon-button" aria-haspopup="menu" title="Scan this folder, administration, your account and more" aria-controls="menu" aria-label="More"
                [attr.aria-expanded]="isOpen()" (click)="popovers.toggle('menu')">
            <svg sfeIcon="more"></svg>
        </button>
        <div class="popover menu-list" id="menu" role="menu" [hidden]="!isOpen()">
            <p class="menu-account" role="none">
                <svg sfeIcon="user"></svg>
                <span>
                    {{ user() ? (user()!.displayName || user()!.userName) : 'Guest' }}
                    <small>{{ config()?.tenant?.name }}</small>
                </span>
            </p>
            @if (canScan() || canRemove()) {
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
    protected readonly adminVisible = computed(() => {
        const rights = this.configService.rights();
        return visibleSections(any => any.some(right => rights.has(right))).length > 0;
    });
    protected readonly otherTenants = computed(() => {
        const config = this.config();
        return config?.tenants.filter(tenant => tenant.id !== config.tenant?.id) ?? [];
    });
    protected readonly versionTitle = computed(() => {
        const config = this.config();
        return config?.commit ? `Version ${config.version}, commit ${config.commit}` : `Version ${config?.version}`;
    });

    protected readonly startScan = () => this.scan.start();
    protected readonly removeFromIndex = () => this.dialogs.removeFromIndex()?.open();
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
