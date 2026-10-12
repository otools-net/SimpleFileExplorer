import { APP_BASE_HREF } from '@angular/common';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withRouterConfig } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
    providers: [
        provideBrowserGlobalErrorListeners(),
        provideHttpClient(withFetch()),
        // Going to what is listed (a sidebar tag searched again, the folder's crumb) reloads it, as a new listing.
        // The search box doesn't for its own query (SearchBoxComponent.searchFor): that would lose the scroll position.
        provideRouter(routes, withRouterConfig({ onSameUrlNavigation: 'reload' })),
        // index.html has <base href="./">: the app's root is where the page was served from, also under a virtual
        // directory (e.g. /sfe/).
        { provide: APP_BASE_HREF, useFactory: () => new URL(document.baseURI).pathname },
    ],
};
