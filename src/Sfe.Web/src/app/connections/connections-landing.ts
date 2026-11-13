import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router, UrlTree } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Api } from '../core/api.service';

/** What the server's redirect says went wrong (`connections?error=`). */
export const LANDING_ERRORS: Record<string, string> = {
    'signed-out': 'You were signed out meanwhile: sign in, then connect again.',
    refused: 'The provider did not give access: nothing was connected.',
    expired: 'This sign-in is over (too late, or already used): start again.',
    failed: 'The provider refused the sign-in: nothing was connected.',
};

/**
 * `connections?made=` / `?reconnected=` / `?error=`: where the server sends the browser back after signing in at a
 * provider (`ConnectionSignInEndpoints`). Says what happened and opens the page of that connection (My account for
 * one's own, Administration › Connections for the others).
 */
export const connectionsLanding: CanActivateFn = async (route: ActivatedRouteSnapshot): Promise<UrlTree> => {
    const router = inject(Router);
    const toasts = inject(ToastService);
    const config = inject(ConfigService);
    const query = route.queryParamMap;
    const id = Number(query.get('made') ?? query.get('reconnected'));

    if (!id) {
        toasts.error(LANDING_ERRORS[query.get('error') ?? ''] ?? LANDING_ERRORS['failed']);
        return pageFor(router, config.can(Rights.ownConnections) ? 'account' : 'library');
    }

    const level = await levelOf(inject(Api), id);
    if (query.has('made')) toasts.show('Connected.');
    else if (query.get('otherAccount') === 'true')
        toasts.show('Signed in again, to another account than before: who uses it now sees other files.');
    else toasts.show('Signed in again.');
    return pageFor(router, level);
};

async function levelOf(api: Api, id: number): Promise<string> {
    try {
        const view = await api.connections();
        return view.managed.find((m) => m.connection.id === id)?.connection.level ?? 'account';
    } catch {
        return 'account';
    }
}

function pageFor(router: Router, level: string): UrlTree {
    return level === 'account'
        ? router.createUrlTree(['/admin'], { queryParams: { section: 'account', page: 'connections' } })
        : router.createUrlTree(['/admin'], { queryParams: { section: 'connections', page: 'connections' } });
}
