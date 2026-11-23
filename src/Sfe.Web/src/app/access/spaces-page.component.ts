import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { TenantDetails } from './access.models';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { SpaceDetailComponent, SpaceView } from './space-detail.component';
import { SpaceEditorComponent } from './space-editor.component';

/** The model in four tiles, above the spaces: what a space holds. */
@Component({
    selector: 'sfe-access-model',
    imports: [IconComponent, HelpComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'access-model' },
    template: `
        <div class="help-row access-model-head">
            <p i18n>Each <strong>space</strong> holds:</p>
            <sfe-help about="How spaces work" i18n-about>
                <p i18n>A space is a separate home for one library on this server. Everything about the library belongs
                    to the space: its files, index, tags and settings, and who may see it.</p>
                <p i18n>An example server:</p>
                <pre class="access-example" i18n="example of two spaces, keep the drawing">Space “Family”
├─ Library   D:\\Media\\Family
├─ Groups    Administrators · Users · Guests · Kids
├─ Accounts  anna → Administrators
│            tom  → Users, Kids
└─ Visitors  not let in

Space “Holiday photos”
├─ Library   D:\\Media\\Holidays
├─ Groups    Administrators · Users · Guests
├─ Accounts  anna → Users
└─ Visitors  let in, with the rights of Guests</pre>
                <p i18n>Anna sees both spaces and switches between them from the ⋮ menu; Tom sees only Family; anyone on
                    the network can look at the holiday photos without an account. Server administrators stand above
                    every space.</p>
            </sfe-help>
        </div>
        <ol class="access-model-tiles">
            <li><svg sfeIcon="folder"></svg><strong i18n>A library</strong>
                <span i18n>One folder of the server, with its files, index, tags and settings.</span></li>
            <li><svg sfeIcon="group"></svg><strong i18n>Groups</strong>
                <span i18n>Sets of rights: what their members may do in the space.</span></li>
            <li><svg sfeIcon="user"></svg><strong i18n>Accounts</strong>
                <span i18n>People who sign in, each in one or more of the space's groups.</span></li>
            <li><svg sfeIcon="globe"></svg><strong i18n>Visitors</strong>
                <span i18n>If you let them in: people without an account, with the Guests group's
                    rights.</span></li>
        </ol>
    `,
})
export class AccessModelComponent {}

/**
 * Administration › Spaces & accounts › Spaces. Server administrators get every space as a card, and a space's page
 * when one is opened (`space` in the address, so that a reload stays there); a space's administrator gets their own
 * space's page directly.
 */
@Component({
    selector: 'sfe-spaces-page',
    imports: [IconComponent, AccessModelComponent, SpaceDetailComponent, SpaceEditorComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'access-page' },
    templateUrl: './spaces-page.component.html',
})
export class SpacesPageComponent implements OnInit {
    private readonly api = inject(Api);
    protected readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly location = inject(Location);
    protected readonly editor = viewChild.required(SpaceEditorComponent);

    /** Every space (server administrators); empty for a space's administrator. */
    protected readonly spaces = signal<TenantDetails[]>([]);
    /** The session's space, for a space's administrator. */
    private readonly own = signal<SpaceView | null>(null);
    protected readonly loaded = signal(false);
    protected readonly error = signal<string | null>(null);

    protected readonly serverWide = computed(() => this.config.can(Rights.manageTenants));
    private readonly params = toSignal(this.route.queryParamMap, { requireSync: true });
    protected readonly selected = computed<SpaceView | null>(() => {
        if (!this.serverWide()) return this.own();
        const id = Number(this.params().get('space'));
        const space = this.spaces().find((candidate) => candidate.id === id);
        return space ? this.viewOf(space) : null;
    });

    ngOnInit(): Promise<void> {
        return this.load();
    }

    async load(): Promise<void> {
        try {
            if (this.serverWide()) this.spaces.set(await this.api.tenants());
            else {
                const access = await this.api.tenantAccess();
                this.own.set({
                    id: access.id,
                    name: access.name,
                    root: null,
                    guestAccess: access.guestAccess,
                    isDefault: false,
                    disabled: false,
                    current: true,
                });
            }
            this.error.set(null);
        } catch (error) {
            this.error.set((error as Error).message);
        } finally {
            this.loaded.set(true);
        }
    }

    protected details(id: number): TenantDetails | undefined {
        return this.spaces().find((space) => space.id === id);
    }

    /** Shows a space's page (null: the list), in place of the current entry, keeping where "back" leads. */
    protected openSpace(id: number | null): void {
        void this.router.navigate([], {
            relativeTo: this.route,
            queryParams: { space: id },
            queryParamsHandling: 'merge',
            replaceUrl: true,
            state: (this.location.getState() as Record<string, unknown> | null) ?? {},
        });
    }

    protected async onSaved(space: TenantDetails): Promise<void> {
        const created = !this.spaces().some((existing) => existing.id === space.id);
        await this.load();
        if (created) this.openSpace(space.id);
    }

    protected onChanged(space: SpaceView): void {
        if (this.serverWide())
            this.spaces.update((spaces) =>
                spaces.map((existing) =>
                    existing.id === space.id ? { ...existing, guestAccess: space.guestAccess } : existing,
                ),
            );
        else this.own.set(space);
    }

    protected async remove(space: TenantDetails): Promise<void> {
        if (
            !confirm(
                $localize`Delete the space “${space.name}:name:”? Its groups and settings go, and its accounts lose \
access to it (the accounts themselves stay). The index of its library and its caches stay on the server's disk. \
This cannot be undone.`,
            )
        )
            return;
        try {
            await this.api.deleteTenant(space.id);
            this.toasts.show($localize`Space “${space.name}:name:” deleted.`);
            this.openSpace(null);
            await this.load();
            await this.config.load();
        } catch (error) {
            const reason = (error as Error).message;
            this.toasts.error($localize`Could not delete the space “${space.name}:name:”: ${reason}:reason:`);
        }
    }

    private viewOf(space: TenantDetails): SpaceView {
        return {
            id: space.id,
            name: space.name,
            root: space.root,
            guestAccess: space.guestAccess,
            isDefault: space.isDefault,
            disabled: space.disabled,
            current: space.id === this.config.tenant()?.id,
        };
    }
}
