import { Routes } from '@angular/router';
import { leaveAdminPage } from './admin/leave-admin-page';
import { LibraryPageComponent } from './library/library-page.component';
import { PlayerPageComponent } from './player/player-page.component';

/**
 * The listing page at the root (its query string says what it lists), the administration page, the video player
 * alone, the sign-in page and the setup page (the first administrator). All are one level deep, so the relative
 * <base href="./"> holds under a virtual directory. The administration, sign-in and setup pages load when first
 * opened: the listing doesn't need them.
 */
export const routes: Routes = [
    { path: '', component: LibraryPageComponent },
    {
        path: 'admin',
        loadComponent: () => import('./admin/admin-page.component').then(m => m.AdminPageComponent),
        canDeactivate: [leaveAdminPage],
    },
    // Its earlier address, still in bookmarks.
    { path: 'config', redirectTo: 'admin' },
    { path: 'player', component: PlayerPageComponent },
    { path: 'login', loadComponent: () => import('./account/login-page.component').then(m => m.LoginPageComponent) },
    { path: 'setup', loadComponent: () => import('./account/setup-page.component').then(m => m.SetupPageComponent) },
    { path: '**', redirectTo: '' },
];
