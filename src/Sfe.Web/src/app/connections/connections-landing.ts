import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router, UrlTree } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { Api } from '../core/api.service';

/** What the server's redirect says went wrong (`connections?error=`). */
export const LANDING_ERRORS: Record<string, string> = {
    'signed-out': $localize`Could not connect: you were signed out in the meantime. Sign in, then connect again.`,
    refused: $localize`Could not connect: the storage didn't give access.`,
    expired: $localize`Could not connect: this sign-in has expired or was already used. Start again.`,
    failed: $localize`Could not connect: the storage refused the sign-in.`,
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
    if (query.has('made')) toasts.show($localize`Connection added.`);
    else if (query.get('otherAccount') === 'true')
        toasts.show(
            $localize`Signed in again, to another account than before. Whoever uses the connection now sees \
other files.`,
        );
    else toasts.show($localize`Signed in again.`);
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
