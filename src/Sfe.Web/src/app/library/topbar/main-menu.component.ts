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
 * The yellow ! also shows, for everyone, while the server doesn't answer (`ChangeFeed.serverLost`), with its own line.
 */
@Component({
    selector: 'sfe-main-menu',
    imports: [IconComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'menu' },
    templateUrl: './main-menu.component.html',
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
        return (
            !!listing && !this.store.searching() && !!this.store.route().path && this.store.can(Rights.excludeFolders)
        );
    });
    protected readonly adminVisible = computed(() => {
        const rights = this.configService.rights();
        return visibleSections((any) => any.some((right) => rights.has(right))).length > 0;
    });
    /** The account line links to Administration › My account when signed in with one of its rights. */
    protected readonly accountVisible = computed(() => {
        const rights = this.configService.rights();
        return (
            !!this.user() &&
            visibleSections((any) => any.some((right) => rights.has(right))).some((section) => section.id === 'account')
        );
    });
    protected readonly otherTenants = computed(() => {
        const config = this.config();
        return config?.tenants.filter((tenant) => tenant.id !== config.tenant?.id) ?? [];
    });
    /** How keep alive goes (the change feed): a dot on the button, a line in the menu; nothing when it is off. */
    protected readonly keepAlive = inject(ChangeFeed).keepAlive;
    /** The server doesn't answer (the change feed is lost): the yellow ! and a line of its own, for everyone. */
    protected readonly serverLost = inject(ChangeFeed).serverLost;
    protected readonly warning = computed(() => this.serverLost() || this.keepAlive() === 'failing');
    protected readonly keepAliveText = computed(() =>
        this.keepAlive() === 'failing' ? 'Network folder not answering' : 'Network folder answering',
    );
    protected readonly canSeeKeepAlive = computed(() => this.configService.rights().has(Rights.keepAliveSettings));
    protected readonly buttonTitle = computed(() => {
        const title = 'Scan this folder, administration, your account and more';
        if (this.serverLost()) return `${title} · Server not answering`;
        return this.keepAlive() === 'none' ? title : `${title} · ${this.keepAliveText()}`;
    });
    protected readonly buttonLabel = computed(() =>
        this.serverLost()
            ? 'More (server not answering)'
            : this.keepAlive() === 'failing'
              ? 'More (network folder not answering)'
              : 'More',
    );
    protected readonly versionTitle = computed(() => {
        const config = this.config();
        return config?.commit ? `Version ${config.version}, commit ${config.commit}` : `Version ${config?.version}`;
    });

    protected readonly startScan = () => this.scan.start();
    protected readonly removeFromIndex = () => this.dialogs.removeFromIndex()?.open();
    protected readonly excludeFolder = () => {
        const listing = this.store.listing();
        if (listing && 'exclusions' in listing) this.dialogs.folderExclusions()?.open(listing.path, listing.exclusions);
        else this.dialogs.folderExclusions()?.open(this.store.route().path, null);
    };
    protected readonly toggleFullscreen = () => {
        if (document.fullscreenElement) void document.exitFullscreen();
        else void document.documentElement.requestFullscreen?.();
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
