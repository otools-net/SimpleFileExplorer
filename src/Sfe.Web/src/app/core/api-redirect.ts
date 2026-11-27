import { Router } from '@angular/router';

/**
 * Opens the page a refused request calls for (`Api.send`): the sign-in page for want of a session (401), the setup
 * page before the server is set up (503 `setup-required`), the update page while the install waits for its update
 * (503 `migration-required`), the page saying a Local server answers its own computer only (403 `local-only`).
 * Anything else: nothing.
 */
export function redirectAfterRefusal(router: Router, status: number, type: string | null): void {
    if (
        status !== 401 &&
        !(status === 503 && (type === 'setup-required' || type === 'migration-required')) &&
        !(status === 403 && type === 'local-only')
    )
        return;
    const page = router.url.split('?')[0];
    if (status === 403 && type === 'local-only') {
        if (page !== '/local-only' && page !== '/login') void router.navigate(['/local-only']);
    } else if (status === 503 && type === 'migration-required') {
        if (page !== '/migration') void router.navigate(['/migration']);
    } else if (status === 503 && type === 'setup-required' && page !== '/setup') void router.navigate(['/setup']);
    else if (status === 401 && page !== '/login' && page !== '/setup' && page !== '/migration')
        void router.navigate(['/login'], { queryParams: { return: router.url === '/' ? null : router.url } });
}
