import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConnectionKind } from '../../core/config.models';
import { ConfigService } from '../../core/config.service';
import { Rights } from '../../core/rights';
import { injectWizard } from '../wizard-context';
import { AddConnectionDraft, kindOf, withMethod } from './add-connection-draft';

/** The kind of storage: one of those this server offers (a kind is offered once it is set up, Dropbox's app key). */
@Component({
    selector: 'sfe-add-connection-kind',
    imports: [RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (kinds().length) {
            <fieldset class="access-choices">
                <legend class="field-label" i18n="the kind of storage, such as Dropbox">Kind</legend>
                @for (kind of kinds(); track kind.kind) {
                    <label class="check">
                        <input
                            type="radio"
                            name="add-connection-kind"
                            [value]="kind.kind"
                            [checked]="wizard.draft().kind === kind.kind"
                            (change)="pick(kind)"
                        >
                        <span>{{ kind.name }}</span>
                    </label>
                }
            </fieldset>
        } @else {
            <p class="field-hint" data-no-kinds i18n>No kind of connection is set up on this server yet: a kind is
                offered once whoever runs the server has registered SFE at the provider (Dropbox: its app key).</p>
            @if (setsUp()) {
                <p>
                    <a
                        routerLink="/admin"
                        [queryParams]="{ section: 'connections', page: 'setup' }"
                        title="How to register SFE at Dropbox, and where its app key goes"
                        i18n-title
                        i18n
                    >Set up Dropbox</a>
                </p>
            }
        }
        @if (wizard.errors()['kind']; as error) {
            <span class="field-error">{{ error }}</span>
        }
    `,
})
export class AddConnectionKindStep {
    private readonly config = inject(ConfigService);
    protected readonly wizard = injectWizard<AddConnectionDraft>();
    protected readonly kinds = computed(() => this.config.config()?.connections?.kinds ?? []);
    protected readonly setsUp = computed(() => this.config.can(Rights.serverSettings));

    /** A kind, with its first way to sign in; the name follows the kind until the person types one. */
    protected pick(kind: ConnectionKind): void {
        const draft = this.wizard.draft();
        const named = draft.name && draft.name !== kindOf(this.kinds(), draft.kind)?.name;
        this.wizard.change({
            kind: kind.kind,
            kindName: kind.name,
            name: named ? draft.name : kind.name,
            ...withMethod(kind, undefined),
        });
    }
}
