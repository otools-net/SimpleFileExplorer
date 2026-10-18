import { Routes } from '@angular/router';
import { leaveAdminPage } from './admin/leave-admin-page';
import { LibraryPageComponent } from './library/library-page.component';
import { PlayerPageComponent } from './player/player-page.component';

/**
 * The listing page at the root (its query string says what it lists), the administration page, the video player
 * alone, a file in its viewer (`view`: text...), the sign-in page, the setup page (the first administrator) and the
 * update page (after an upgrade). All are one level deep, so the relative <base href="./"> holds under a virtual
 * directory. The administration, view, sign-in, setup and update pages load when first opened: the listing doesn't
 * need them.
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
    // A file in its viewer, on a page of its own (text...): loaded when first opened.
    { path: 'view', loadComponent: () => import('./formats/view-page.component').then(m => m.ViewPageComponent) },
    { path: 'login', loadComponent: () => import('./account/login-page.component').then(m => m.LoginPageComponent) },
    { path: 'setup', loadComponent: () => import('./account/setup-page.component').then(m => m.SetupPageComponent) },
    // The update of an install to this version: the only page working until it is done.
    { path: 'migration', loadComponent: () => import('./migration/migration-page.component').then(m => m.MigrationPageComponent) },
    { path: '**', redirectTo: '' },
];
