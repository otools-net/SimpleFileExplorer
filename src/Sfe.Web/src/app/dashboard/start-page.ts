import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router, UrlTree } from '@angular/router';
import { redirected } from '../core/api.service';
import { ClientConfig } from '../core/config.models';
import { ConfigService } from '../core/config.service';
import { HomeFolderService } from '../library/home-folder/home-folder.service';

/**
 * Whether the page was opened on a history entry of its own (a reload, back from another site): the router's state
 * is in it. Read before the router replaces it.
 */
let openedInApp = hasNavigationId(history.state);

/** The configuration the guard loaded for the listing it let through: the page uses it rather than asking again. */
let loaded: ClientConfig | null = null;

/**
 * The guard of the listing page's address alone (`/`, no query), on the app's first navigation from a fresh entry (a
 * bookmark, a typed address, a new tab): opens the start page the user chose (`browse.startPage`), the dashboard or
 * the home folder; the top of the library stays. Within the app, `/` is the top of the library (its crumb, the
 * brand), and a reload keeps what was shown.
 */
export const startPage: CanActivateFn = async (route: ActivatedRouteSnapshot): Promise<boolean | UrlTree> => {
    // Injected before waiting: the injection context ends at the first await.
    const router = inject(Router);
    const config = inject(ConfigService);
    const homes = inject(HomeFolderService);
    const fresh = !openedInApp && !router.navigated && route.queryParamMap.keys.length === 0;
    openedInApp = true;
    if (!fresh) return true;
    try {
        loaded = await config.load();
    } catch (error) {
        // The sign-in or setup page is opening; anything else, the listing page says.
        return !redirected(error);
    }
    if (loaded.startPage === 'dashboard') {
        loaded = null;
        return router.createUrlTree(['/dashboard']);
    }
    if (loaded.startPage === 'home') return homeFolder(router, homes);
    return true;
};

/** The home folder's listing; the top of the library without one, or when it is not there. */
async function homeFolder(router: Router, homes: HomeFolderService): Promise<boolean | UrlTree> {
    await homes.load();
    const home = homes.home();
    if (!home?.path || !home.exists) return true;
    return router.createUrlTree(['/'], { queryParams: { path: home.path } });
}

/** The configuration the start page guard just loaded, once; null when it loaded none. */
export function takeStartConfig(): ClientConfig | null {
    const config = loaded;
    loaded = null;
    return config;
}

/** For specs: the page as just opened, on an entry of its own (`inApp`) or a fresh one. */
export function resetStartPage(inApp = false): void {
    openedInApp = inApp;
    loaded = null;
}

function hasNavigationId(state: unknown): boolean {
    return !!state && typeof state === 'object' && 'navigationId' in state;
}
