import {
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    inject,
    output,
    signal,
    viewChild,
} from '@angular/core';
import { fieldErrors, toggled } from '../access/access-parts';
import { RightDefinition } from '../access/access.models';
import { Api } from '../core/api.service';
import { AccessTokenInfo } from '../core/access-token.models';
import { ConfigService } from '../core/config.service';
import { copyText } from '../core/copy-path.service';
import { Rights } from '../core/rights';
import { ToastService } from '../core/toast.service';
import { checkedOf, valueOf } from '../shared/events';
import { SheetComponent } from '../shared/sheet.component';
import { expiryChoices, expiryOf, rightAreas, tokensConfig } from './token-parts';

/** The fields of the form, under which the server's refusals show. */
const FORM_FIELDS = ['name', 'expires', 'tenantId', 'rights'];

/** The rights that may read the catalog of rights (their titles and areas). */
const CATALOG_RIGHTS = [Rights.manageMembers, Rights.manageUsers, Rights.manageTenants];

/**
 * Makes a personal access token: its name, life, library and rights (among the account's own), then shows the token
 * once, with Copy. The token is never kept: closing the sheet, or leaving the page, forgets it.
 */
@Component({
    selector: 'sfe-token-create',
    imports: [SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './token-create.component.html',
})
export class TokenCreateComponent {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    protected readonly sheet = viewChild.required<SheetComponent>('sheet');

    readonly created = output<AccessTokenInfo>();

    protected readonly name = signal('');
    /** The life chosen, in days; null: the longest allowed. */
    protected readonly days = signal<number | null>(null);
    protected readonly tenantId = signal<number | null>(null);
    protected readonly everyRight = signal(true);
    protected readonly chosen = signal<ReadonlySet<string>>(new Set());
    protected readonly catalog = signal<RightDefinition[] | null>(null);
    protected readonly errors = signal<Record<string, string>>({});
    protected readonly problem = signal<string | null>(null);
    protected readonly saving = signal(false);
    /** The whole token, shown once after it was made. */
    protected readonly secret = signal<string | null>(null);
    protected readonly heading = computed(() =>
        this.secret() ? $localize`Your new token` : $localize`New access token`,
    );

    protected readonly choices = computed(() => expiryChoices(tokensConfig(this.config.config())?.maxLifetimeDays));
    protected readonly tenants = computed(() => this.config.config()?.tenants ?? []);
    protected readonly areas = computed(() => rightAreas(this.config.config()?.rights ?? [], this.catalog()));

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;

    constructor() {
        inject(DestroyRef).onDestroy(() => this.forget());
    }

    /** Opens the sheet on a new token: 90 days when allowed, every library, every right. */
    open(): void {
        this.forget();
        const choices = this.choices();
        this.days.set(choices.some((choice) => choice.days === 90) ? 90 : choices[choices.length - 1].days);
        this.tenantId.set(null);
        this.everyRight.set(true);
        this.chosen.set(new Set());
        this.errors.set({});
        this.problem.set(null);
        this.sheet().open();
        void this.loadCatalog();
    }

    protected chooseDays(event: Event): void {
        const value = valueOf(event);
        this.days.set(value ? Number(value) : null);
    }

    protected chooseTenant(event: Event): void {
        const value = valueOf(event);
        this.tenantId.set(value ? Number(value) : null);
    }

    protected toggleRight(id: string, on: boolean): void {
        this.chosen.update((chosen) => toggled(chosen, id, on));
    }

    protected async create(event?: Event): Promise<void> {
        event?.preventDefault();
        if (this.saving() || !this.name().trim()) return;
        this.saving.set(true);
        this.errors.set({});
        this.problem.set(null);
        try {
            const made = await this.api.createToken({
                name: this.name().trim(),
                expires: expiryOf(this.days()),
                tenantId: this.tenantId(),
                rights: this.everyRight() ? [] : [...this.chosen()],
            });
            this.secret.set(made.secret);
            this.created.emit(made.token);
        } catch (error) {
            this.showError(error);
        } finally {
            this.saving.set(false);
        }
    }

    protected async copy(): Promise<void> {
        const secret = this.secret();
        if (!secret) return;
        try {
            await copyText(secret);
            this.toasts.show('Token copied.');
        } catch {
            this.toasts.error('Could not copy the token: select it and copy it by hand.');
        }
    }

    protected selectAll(event: Event): void {
        (event.target as HTMLInputElement).select();
    }

    /** Forgets the token shown and the name typed (the sheet closed, or the page left). */
    protected forget(): void {
        this.secret.set(null);
        this.name.set('');
    }

    /** The server's refusals under their fields (those of no field shown below the form), or its problem as it is. */
    private showError(error: unknown): void {
        const fields = fieldErrors(error);
        if (!fields) {
            this.problem.set((error as Error).message);
            return;
        }
        this.errors.set(fields);
        const others = Object.entries(fields).filter(([key]) => !FORM_FIELDS.includes(key));
        if (others.length) this.problem.set(others.map(([, message]) => message).join(' '));
    }

    /** The titles of the rights, when the session may read them; their ids otherwise. */
    private async loadCatalog(): Promise<void> {
        if (this.catalog() || !this.config.canAny(CATALOG_RIGHTS)) return;
        try {
            this.catalog.set((await this.api.rights()).rights);
        } catch {
            // The ids are shown instead.
        }
    }
}
