import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Api } from '../../core/api.service';
import { ConfigService } from '../../core/config.service';
import { METHOD_NAMES, problemOf } from '../../connections/sign-in';
import { BusyDirective, whileBusy } from '../../shared/busy.directive';
import { valueOf } from '../../shared/events';
import { injectWizard } from '../wizard-context';
import { requestProblem } from '../wizard-run';
import { AddConnectionDraft, connectRequest, kindOf, requestKey, withMethod } from './add-connection-draft';

/**
 * How to sign in to the storage: at the provider (Finish sends the browser there and it comes back to the wizard), at
 * the provider copying the code it shows (the page opens in a new tab here), or with a name and password (tried on
 * Next).
 */
@Component({
    selector: 'sfe-add-connection-sign-in',
    imports: [BusyDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './add-connection-sign-in.step.html',
})
export class AddConnectionSignInStep {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    protected readonly wizard = injectWizard<AddConnectionDraft>();
    protected readonly opening = signal(false);
    protected readonly problem = signal<string | null>(null);
    protected readonly methodNames = METHOD_NAMES;
    protected readonly value = valueOf;

    private readonly kind = computed(() =>
        kindOf(this.config.config()?.connections?.kinds ?? [], this.wizard.draft().kind),
    );
    protected readonly kindName = computed(() => this.kind()?.name ?? '');
    protected readonly methods = computed(() => this.kind()?.signIn ?? []);
    protected readonly fields = computed(
        () => this.methods().find((method) => method.kind === this.wizard.draft().method)?.fields ?? [],
    );
    /** The code flow's sign-in is begun for what the earlier steps say now. */
    protected readonly begun = computed(() => {
        const draft = this.wizard.draft();
        return draft.state !== '' && draft.begunFor === requestKey(draft);
    });

    protected pick(method: string): void {
        this.wizard.change(withMethod(this.kind(), method));
    }

    protected setField(id: string, value: string): void {
        this.wizard.change({ fields: { ...this.wizard.draft().fields, [id]: value } });
    }

    /** Begins the code flow (nothing is made before the code comes back) and opens the provider's page. */
    protected async openSignIn(): Promise<void> {
        const draft = this.wizard.draft();
        this.problem.set(null);
        try {
            const begun = await whileBusy(this.opening, () => this.api.authorizeConnection(connectRequest(draft)));
            const problem = problemOf(begun);
            if (problem !== null) return this.problem.set(problem);
            if (begun.address) window.open(begun.address, '_blank', 'noopener');
            this.wizard.change({ state: begun.state ?? '', begunFor: requestKey(draft), code: '' });
        } catch (error) {
            this.problem.set(requestProblem(error));
        }
    }
}
