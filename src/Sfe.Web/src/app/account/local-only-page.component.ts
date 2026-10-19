import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { InstallationSectionComponent } from '../admin/installation-section.component';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { SessionInfo } from '../core/models';
import { IconComponent } from '../shared/icon.component';

/** This app's address on the server's own computer: the same port and folder, at localhost. */
export function localAddress(base = document.baseURI): string {
    const url = new URL(base);
    url.hostname = 'localhost';
    url.search = '';
    url.hash = '';
    return url.toString();
}

/**
 * A Local server opened from another computer (`local-only`, after a 403 `local-only`): where to open it instead.
 * Its server administrator may sign in here and switch it to External with the setup code (the installation page,
 * which stays open to other computers): the way out for an install upgraded to Local while used over the network.
 */
@Component({
    selector: 'sfe-local-only-page',
    imports: [IconComponent, InstallationSectionComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'account-page' },
    template: `
        <section class="account-card wide">
            <header class="account-head">
                <span class="brand"><svg sfeIcon="folder"></svg><span>SFE</span></span>
                <h1>SFE is in Local mode</h1>
            </header>
            <p>
                This SFE is set up for the computer it runs on: only that computer opens it, at
                <a class="local-only-address" [href]="address">{{ address }}</a>, and only its administrators sign in.
            </p>
            @if (session() === undefined) {
                <div class="tag-loading"><span class="spinner"></span></div>
            } @else if (session()?.user?.isServerAdmin) {
                <p>To let other devices open it, switch it to <strong>External</strong> below, with the setup code.</p>
                <sfe-installation-section />
                <button type="button" class="button account-submit" title="Open the library" (click)="open()">Open the library</button>
            } @else {
                <p>Do you run this server? Sign in as its administrator to switch it to External, so that other devices can open it.</p>
                <button type="button" class="button primary account-submit" title="Sign in as the server's administrator" (click)="signIn()">Sign in</button>
            }
        </section>
    `,
})
export class LocalOnlyPageComponent implements OnInit {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly router = inject(Router);

    protected readonly address = localAddress();
    /** undefined while asked; null when the server can't be reached (shown as signed out). */
    protected readonly session = signal<SessionInfo | null | undefined>(undefined);

    constructor() {
        inject(Title).setTitle('Local mode – SFE');
    }

    async ngOnInit(): Promise<void> {
        try {
            this.session.set(await this.api.session());
        } catch {
            this.session.set(null);
        }
    }

    protected signIn(): void {
        void this.router.navigate(['/login'], { queryParams: { return: '/local-only' } });
    }

    protected open(): void {
        this.config.clear();
        void this.router.navigate(['/'], { replaceUrl: true });
    }
}
