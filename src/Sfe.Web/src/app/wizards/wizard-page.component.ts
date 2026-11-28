import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { redirected } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { ThemeService } from '../core/theme.service';
import { EmptyStateComponent } from '../shared/empty-state.component';
import { IconComponent } from '../shared/icon.component';
import { WizardComponent } from './wizard.component';
import { WizardDefinition } from './wizard-definition';
import { canOpenWizard, findWizard } from './wizard-registry';

/**
 * The page of a wizard, `wizard?id=…` (one route for all of them, a level deep like every page): the wizard's line in
 * the registry says who may open it, its code loads now. Cancel, Done and the back arrow lead to the administration
 * page it belongs to.
 */
@Component({
    selector: 'sfe-wizard-page',
    imports: [WizardComponent, EmptyStateComponent, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'wizard-page' },
    template: `
        @if (problem(); as problem) {
            <sfe-empty-state icon="lock" [heading]="problem" />
        } @else if (definition(); as definition) {
            <header class="topbar wizard-topbar">
                <button
                    type="button"
                    class="icon-button ghost"
                    [attr.aria-label]="backHint"
                    [title]="backHint"
                    (click)="leave()"
                >
                    <svg sfeIcon="left"></svg>
                </button>
                <span class="brand"><svg sfeIcon="folder"></svg><span translate="no">SFE</span></span>
            </header>
            <sfe-wizard [definition]="definition" [returned]="returned()" (closed)="leave()" />
        } @else {
            <div class="tag-loading"><span class="spinner"></span></div>
        }
    `,
})
export class WizardPageComponent implements OnInit {
    private readonly config = inject(ConfigService);
    private readonly theme = inject(ThemeService);
    private readonly router = inject(Router);
    private readonly title = inject(Title);
    private readonly params = toSignal(inject(ActivatedRoute).queryParamMap, { requireSync: true });
    private readonly wizard = viewChild(WizardComponent);

    protected readonly entry = computed(() => findWizard(this.params().get('id')));
    /** What another site sent back after the wizard's Finish left for it (`made=7`): the query besides `id`. */
    protected readonly returned = computed(() => {
        const params = this.params();
        return Object.fromEntries(params.keys.filter((key) => key !== 'id').map((key) => [key, params.get(key)!]));
    });
    protected readonly definition = signal<WizardDefinition<unknown> | null>(null);
    protected readonly problem = signal<string | null>(null);
    protected readonly backHint = $localize`Back to the administration`;

    async ngOnInit(): Promise<void> {
        const entry = this.entry();
        try {
            const config = this.config.config() ?? (await this.config.load());
            this.theme.setDefault(config.theme);
            if (!entry) return this.problem.set($localize`There is no such wizard.`);
            if (!canOpenWizard(entry, (right) => this.config.can(right), config.mode))
                return this.problem.set($localize`You may not use this wizard.`);
            this.title.setTitle($localize`${entry.title}:wizard: – SFE`);
            this.definition.set(await entry.load());
        } catch (error) {
            if (!redirected(error)) this.problem.set((error as Error).message);
        }
    }

    /** The route's guard: asks before losing what was chosen. */
    canLeave(): boolean {
        return this.wizard()?.canLeave() ?? true;
    }

    /** Back to the administration page the wizard belongs to. */
    protected leave(): void {
        const home = this.entry()?.home;
        void this.router.navigate(['/admin'], { queryParams: { section: home?.section, page: home?.page } });
    }
}
